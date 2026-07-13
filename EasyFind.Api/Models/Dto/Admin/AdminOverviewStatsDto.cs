namespace EasyFind.Api.Models.Dto.Admin;

public class AdminOverviewStatsDto
{
    // Users
    public int TotalUsers { get; set; }
    public int FreeUsers { get; set; }
    public int ProUsers { get; set; }
    public int PremiumUsers { get; set; }
    public int NewUsersLast7Days { get; set; }
    public int NewUsersLast30Days { get; set; }

    // Subscriptions
    public int ActiveSubscriptions { get; set; }

    // Listings
    public int TotalActiveListings { get; set; }
    public int TotalInactiveListings { get; set; }

    // Engagement
    public int TotalApplicationsTracked { get; set; }
    public int TotalBookmarks { get; set; }

    // Revenue (from successful payments)
    public decimal TotalRevenueEtb { get; set; }
    public int SuccessfulPayments { get; set; }
    public int FailedPayments { get; set; }
    public decimal RevenueLast30DaysEtb { get; set; }
}