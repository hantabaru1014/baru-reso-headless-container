using SkyFrost.Base;

namespace Headless.Models;

public class ExtendedWorldStartupParameters : WorldStartupParameters
{
    public IList<string> JoinAllowedUserIds { get; set; } = new List<string>();
}
