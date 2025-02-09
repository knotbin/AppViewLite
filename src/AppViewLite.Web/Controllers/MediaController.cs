using AppViewLite.Models;
using Ipfs;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using System.Buffers;

namespace AppViewLite.Web.Controllers
{
    [Route("/img")]
    [ApiController]
    public class MediaController : ControllerBase
    {
        private readonly BlueskyEnrichedApis apis;

        public MediaController(BlueskyEnrichedApis apis)
        {
            this.apis = apis;
        }

        private readonly static SearchValues<char> CidChars = SearchValues.Create("0123456789abcdefghijklmnopqrstuvwxyz");
        private readonly static bool Enabled = 
            AppViewLiteConfiguration.GetBool(AppViewLiteParameter.APPVIEWLITE_SERVE_IMAGES) ??
            AppViewLiteConfiguration.GetString(AppViewLiteParameter.APPVIEWLITE_CDN) == null;
        private readonly static bool CacheAvatars = AppViewLiteConfiguration.GetBool(AppViewLiteParameter.APPVIEWLITE_CACHE_AVATARS) ?? true;
        private readonly static bool CacheFeedThumbs = AppViewLiteConfiguration.GetBool(AppViewLiteParameter.APPVIEWLITE_CACHE_FEED_THUMBS) ?? false;

        [Route("{size}/plain/{did}/{cid}@jpeg")]
        [HttpGet]
        public async Task GetThumbnail(string size, string did, string cid, [FromQuery] string? pds)
        {
            if (!Enabled) throw new Exception("Image serving is not enabled on this server.");
            var sizeEnum = Enum.Parse<ThumbnailSize>(size);

            var isRawUrl = did.StartsWith("host:", StringComparison.Ordinal);
            if (!isRawUrl)
            {
                BlueskyEnrichedApis.EnsureValidDid(did);

                var pluggable = BlueskyRelationships.TryGetPluggableProtocolForDid(did);

                if (pluggable == null)
                {
                    if (cid.Length != 59) throw new Exception("Invalid CID length.");
                    if (cid.AsSpan().ContainsAnyExcept(CidChars)) throw new Exception("CID contains invalid characters.");
                }
            }

            var sizePixels = sizeEnum switch
            {
                ThumbnailSize.feed_thumbnail => 1000,
                ThumbnailSize.feed_fullsize => 2000,
                ThumbnailSize.avatar => 1000,
                ThumbnailSize.avatar_thumbnail => 150,
                ThumbnailSize.banner => 1000,
                ThumbnailSize.emoji or ThumbnailSize.emoji_profile_name => 64,
                _ => throw new ArgumentException("Unrecognized image size.")
            };

            var storeToDisk =
                sizeEnum is ThumbnailSize.emoji or ThumbnailSize.emoji_profile_name ? true :
                sizeEnum is ThumbnailSize.avatar_thumbnail ? CacheAvatars :
                sizeEnum is ThumbnailSize.feed_thumbnail or ThumbnailSize.banner ? CacheFeedThumbs : 
                false;

            if (storeToDisk)
            {
                ReadOnlySpan<char> shortDid;
                const int PAYLOAD_CUT = 3;
                int shortDidCut;
                if (isRawUrl)
                {
                    // host:example.com -> host:exa/mple.com
                    shortDid = did;
                    shortDidCut = 8;
                }
                else if (did.StartsWith("did:plc:", StringComparison.Ordinal))
                {
                    // did:plc:aaaaaaaaaa -> aaa/aaaaaaa
                    shortDid = did.AsSpan(8);
                    shortDidCut = PAYLOAD_CUT;
                }
                else
                {
                    // did:other:aaaaaaaaaa -> did:other:aaa/aaaaaaa
                    shortDid = did;
                    var secondColon = did.IndexOf(':', 4);
                    shortDidCut = secondColon + 1 + PAYLOAD_CUT;
                    if (did.StartsWith("did:nostr:npub", StringComparison.Ordinal))
                        shortDidCut += 5; // "npub1" is constant
                }

                shortDidCut = Math.Min(shortDidCut, shortDid.Length);

                if (shortDid.ContainsAny('/', '\\')) throw new Exception();
                var escapedPart1 = EscapeDidForFileSystem(shortDid.Slice(0, shortDidCut));
                var escapedPart2 = EscapeDidForFileSystem(shortDid.Slice(shortDidCut));


                var cacheDirectory = AppViewLiteConfiguration.GetString(AppViewLiteParameter.APPVIEWLITE_IMAGE_CACHE_DIRECTORY) ?? apis.DangerousUnlockedRelationships.BaseDirectory + "/image-cache";
                var ext = ".jpg";
                var filename = escapedPart2 + "_" + cid + ext;
                if (filename.Length + 4 >= 255)
                    filename = escapedPart2 + "_-" + Base32.ToBase32(System.Security.Cryptography.SHA256.HashData(Base32.FromBase32(cid))) + ext;
                var cachePath = Path.Combine(cacheDirectory, sizeEnum.ToString(), escapedPart1, filename);


                if (!System.IO.File.Exists(cachePath))
                {
                    using var image = await GetImageAsync(did, cid, pds, sizePixels, sizeEnum);

                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    try
                    {
                        using (var cacheStream = new System.IO.FileStream(cachePath + ".tmp", FileMode.Create, FileAccess.Write))
                        {
                            await WriteImageAsync(image, cacheStream);
                        }
                        System.IO.File.Move(cachePath + ".tmp", cachePath);
                    }
                    catch when (System.IO.File.Exists(cachePath)) 
                    { 
                        // concurrent request already saved this file, this is ok.
                    }
                }

                SetMediaHeaders(cid);

                using var stream = new FileStream(cachePath, new FileStreamOptions 
                { 
                    Mode = FileMode.Open,
                    Share = FileShare.Read | FileShare.Delete,
                    Access = FileAccess.Read,
                    Options = FileOptions.Asynchronous
                });
                await stream.CopyToAsync(Response.Body);

            }
            else
            {
                using var image = await GetImageAsync(did, cid, pds, sizePixels, sizeEnum);
                SetMediaHeaders(cid);
                await WriteImageAsync(image, Response.Body);
            }
        }

        private static async Task WriteImageAsync(Image<Rgba32> image, Stream cacheStream)
        {
            using (image)
            {
                await image.SaveAsWebpAsync(cacheStream, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder { Quality = 70 });
            }
        }

        private static string EscapeDidForFileSystem(ReadOnlySpan<char> did)
        {
            return did.ToString()
                .Replace("_", "__")
                .Replace(':', '_')
                .Replace('.', ',') /* avoids CON.com_etcetera issue on windows */;
        }

        private async Task<Image<Rgba32>> GetImageAsync(string did, string cid, string? pds, int sizePixels, ThumbnailSize sizeEnum)
        {
            var bytes = await apis.GetBlobAsync(did, cid, pds, sizeEnum);
            if (!StartsWithAllowlistedMagicNumber(bytes)) throw new Exception("Unrecognized image format.");
            var image = SixLabors.ImageSharp.Image.Load<Rgba32>(bytes);

            if (Math.Max(image.Width, image.Height) > sizePixels)
            {
                image.Mutate(m => m.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new SixLabors.ImageSharp.Size(sizePixels, sizePixels) }));
            }

            if (sizeEnum == ThumbnailSize.emoji_profile_name)
            {
                EnsureNotConfusableWithVerifiedBadge(ref image);
            }
            return image;
        }

        private readonly static Rgba32 Color_VerifiedGeneric = new Rgba32(0x1D, 0xA1, 0xF2);
        private readonly static Rgba32 Color_VerifiedOrganization = new Rgba32(0xE2, 0xB7, 0x19);
        private readonly static Rgba32 Color_VerifiedGovernment = new Rgba32(0x82, 0x9A, 0xAB);
        private const double VerifiedBadgeThreshold = 0.9;
        private const int ColorComponentDeltaThreshold = 30;
        private static bool AreColorsSimilar(Rgba32 a, Rgba32 b)
        {
            var deltaR = Math.Abs((int)a.R - b.R);
            var deltaG = Math.Abs((int)a.G - b.G);
            var deltaB = Math.Abs((int)a.B - b.B);
            return
                deltaR < ColorComponentDeltaThreshold &&
                deltaG < ColorComponentDeltaThreshold &&
                deltaB < ColorComponentDeltaThreshold;
        }
        private static Rgba32 BlendWithWhiteBackground(Rgba32 color)
        {
            byte alpha = color.A;

            if (alpha == 255) return new Rgba32(color.R, color.G, color.B, 255);
            if (alpha == 0) return new Rgba32(255, 255, 255, 255);

            float alphaFactor = alpha / 255f;

            byte r = (byte)(color.R * alphaFactor + 255 * (1 - alphaFactor));
            byte g = (byte)(color.G * alphaFactor + 255 * (1 - alphaFactor));
            byte b = (byte)(color.B * alphaFactor + 255 * (1 - alphaFactor));

            return new Rgba32(r, g, b, 255);
        }
        public static void EnsureNotConfusableWithVerifiedBadge(ref Image<Rgba32> image)
        {
            using var small = image.Clone(m => m.Resize(new ResizeOptions 
            {
                Mode = ResizeMode.Max,
                Size = new SixLabors.ImageSharp.Size(48, 48),
                Sampler = new NearestNeighborResampler(),
            }));
            int pixelsTotal = 0;
            var pixelsVerifiedGeneric = 0;
            var pixelsVerifiedOrganization = 0;
            var pixelsVerifiedGovernment = 0;
            var pixelsWhite = 0;
            var pixelsNonWhite = 0;

            small.ProcessPixelRows(x =>
            {
                for (int row = 0; row < x.Height; row++)
                {
                    var rowSpan = x.GetRowSpan(row);
                    for (int col = 0; col < rowSpan.Length; col++)
                    {
                        var pixel = rowSpan[col];
                        Rgba32 debugColor;
                        if (pixel.A < 48)
                        {
                            debugColor = Color.Gray;
                        }
                        else
                        {
                            pixelsTotal++;
                            var opaque = BlendWithWhiteBackground(pixel);
                            

                            if (AreColorsSimilar(opaque, Color.White))
                            {

                                pixelsWhite++;
                                debugColor = Color.Red;
                            }
                            else
                            {
                                pixelsNonWhite++;

                                if (AreColorsSimilar(opaque, Color_VerifiedGeneric))
                                {
                                    pixelsVerifiedGeneric++;
                                    debugColor = Color.Green;
                                }
                                else if (AreColorsSimilar(opaque, Color_VerifiedOrganization))
                                {
                                    pixelsVerifiedOrganization++;
                                    debugColor = Color.Blue;
                                }
                                else if (AreColorsSimilar(opaque, Color_VerifiedGovernment))
                                {
                                    pixelsVerifiedGovernment++;
                                    debugColor = Color.DeepPink;
                                }
                                else
                                {
                                    debugColor = opaque;
                                }
                            }
                        }

                        //small[col, row] = debugColor;
                    }

                }
            });

            if (pixelsNonWhite < 10) return;

            // 0.087 for verified badge
            var whitePixelsRatio = (float)pixelsWhite / pixelsTotal; 

            // 0.996 for verified badge
            var verifiedGenericRatio = (float)pixelsVerifiedGeneric / pixelsNonWhite;
            var verifiedOrganizationRatio = (float)pixelsVerifiedOrganization / pixelsNonWhite;
            var verifiedGovernmentRatio = (float)pixelsVerifiedGovernment / pixelsNonWhite;

            if (verifiedGenericRatio > VerifiedBadgeThreshold ||
                verifiedOrganizationRatio > VerifiedBadgeThreshold)
            {
                image.Mutate(m => m.Saturate(0.3f).Opacity(0.3f));
                RemoveOldPalette(ref image);
            }
            else if (verifiedGovernmentRatio > VerifiedBadgeThreshold)
            {
                image.Mutate(m => m.Brightness(0.3f));
                RemoveOldPalette(ref image);
            }

        }

        private static void RemoveOldPalette(ref Image<Rgba32> image)
        {
            // https://github.com/SixLabors/ImageSharp/issues/2865
            var other = new Image<Rgba32>(image.Width, image.Height);
            var image_ = image;
            other.Mutate(m => m.DrawImage(image_, 1));
            image.Dispose();
            image = other;
        }

        private void SetMediaHeaders(string cid)
        {
            Response.ContentType = "image/jpeg";
            Response.Headers.CacheControl = new(["public, max-age=31536000, immutable"]);
            Response.Headers.Pragma = new(["cache"]);
            Response.Headers.ETag = new(["permanent"]);
            Response.Headers.Expires = new(["Fri, 31 Dec 9999 23:59:59 GMT"]);
            Response.Headers.ContentDisposition = new(["inline; filename=\"" + cid + ".jpg\""]);
        }

        private static ReadOnlySpan<byte> Magic_JPG => [0xff, 0xd8, 0xff];
        private static ReadOnlySpan<byte> Magic_PNG => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        private static ReadOnlySpan<byte> Magic_RIFF => "RIFF"u8;
        private static ReadOnlySpan<byte> Magic_WEBP => [0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38];
        private static ReadOnlySpan<byte> Magic_GIF87a => "GIF87a"u8;
        private static ReadOnlySpan<byte> Magic_GIF89a => "GIF89a"u8;
        private static bool StartsWithAllowlistedMagicNumber(ReadOnlySpan<byte> bytes)
        {
            return 
                bytes.StartsWith(Magic_JPG) ||
                bytes.StartsWith(Magic_PNG) ||
                bytes.StartsWith(Magic_WEBP) ||
                bytes.StartsWith(Magic_GIF87a) ||
                bytes.StartsWith(Magic_GIF89a) ||
                (bytes.StartsWith(Magic_RIFF) && bytes.Slice(8).StartsWith(Magic_WEBP))
                ;
        }
    }
}

