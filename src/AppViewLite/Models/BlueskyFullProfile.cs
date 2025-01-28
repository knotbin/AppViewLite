using System.Collections.Generic;

namespace AppViewLite.Models
{
    public class BlueskyFullProfile
    {
        public BlueskyProfile Profile;
        public long Following;
        public long Followers;
        public List<BlueskyProfile>? FollowedByPeopleYouFollow;
        public bool HasFeeds;
        public bool HasLists;
    }
}

