using BrewHaven.Models;

namespace BrewHaven.Helpers;

public static class Session
{
    public static UserAccount? CurrentUser { get; set; }
}
