using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppViewLite
{
    public static class AppViewLiteConfiguration
    {
        public static string? GetString(AppViewLiteParameter parameter)
        {
            if (parameter == default) throw new ArgumentException();
            return Environment.GetEnvironmentVariable(parameter.ToString());
        }

        public static string[]? GetStringList(AppViewLiteParameter parameter)
        {
            var s = GetString(parameter);
            if (s == null) return null;
            return s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        public static int? GetInt32(AppViewLiteParameter parameter)
        {
            var s = GetString(parameter);
            return s != null ? int.Parse(s) : null;
        }
        public static bool? GetBool(AppViewLiteParameter parameter)
        {
            var s = GetString(parameter)?.ToLowerInvariant();
            return s switch
            {
                "1" or "y" or "true" => true,
                "0" or "n" or "false" => false,
                null => null,
                _ => throw new Exception($"Unparseable boolean configuration: {parameter}={s}")
            };
        }
    }

    public enum AppViewLiteParameter
    { 
        None,
        APPVIEWLITE_DIRECTORY,
        APPVIEWLITE_WIKIDATA_VERIFICATION,
        APPVIEWLITE_PLC_DIRECTORY_BUNDLE,
        APPVIEWLITE_READONLY,
        APPVIEWLITE_CDN,
        APPVIEWLITE_DNS_SERVER,
        APPVIEWLITE_USE_DNS_OVER_HTTPS,
        APPVIEWLITE_HANDLE_TO_DID_MAX_STALE_HOURS,
        APPVIEWLITE_DID_DOC_MAX_STALE_HOURS,
        APPVIEWLITE_IMAGE_CACHE_DIRECTORY,
        APPVIEWLITE_CACHE_AVATARS,
        APPVIEWLITE_CACHE_FEED_THUMBS,
        APPVIEWLITE_SERVE_IMAGES,
        APPVIEWLITE_PLC_DIRECTORY,
        APPVIEWLITE_DID_DOC_OVERRIDES,
        APPVIEWLITE_ALLOW_PUBLIC_READONLY_FAKE_LOGIN,
        APPVIEWLITE_LISTEN_TO_PLC_DIRECTORY,
        APPVIEWLITE_LISTEN_TO_FIREHOSE,
        APPVIEWLITE_FIREHOSES,
        APPVIEWLITE_PRINT_LONG_READ_LOCKS_MS,
        APPVIEWLITE_PRINT_LONG_WRITE_LOCKS_MS,
        APPVIEWLITE_PRINT_LONG_UPGRADEABLE_LOCKS_MS,
        APPVIEWLITE_LABEL_FIREHOSES,
        APPVIEWLITE_LISTEN_ACTIVITYPUB_RELAYS,
        APPVIEWLITE_LISTEN_NOSTR_RELAYS,
        APPVIEWLITE_FATAL_ERROR_LOG_DIRECTORY,
        APPVIEWLITE_YOTSUBA_HOSTS,
        APPVIEWLITE_BLOCKLIST_PATH,
        APPVIEWLITE_DISABLE_SLICE_GC,
        APPVIEWLITE_GLOBAL_PERIODIC_FLUSH_SECONDS,
        APPVIEWLITE_TABLE_WRITE_BUFFER_SIZE,
        APPVIEWLITE_NOSTR_IGNORE_REGEX,
        APPVIEWLITE_USE_READONLY_REPLICA,
        APPVIEWLITE_MAX_READONLY_STALENESS_MS_OPPORTUNISTIC,
        APPVIEWLITE_MAX_READONLY_STALENESS_MS_EXPLICIT_READ,
    }
}

