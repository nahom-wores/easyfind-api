namespace EasyFind.Api.Models.Admin;

// Row in the user list
    public class AdminUserListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string Tier { get; set; } = string.Empty;
        public bool HasProfile { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    // Full user detail
    public class AdminUserDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public bool PhoneConfirmed { get; set; }
        public List<string> Roles { get; set; } = [];
        public DateTimeOffset CreatedAt { get; set; }

        // Subscription
        public string Tier { get; set; } = string.Empty;
        public string? SubscriptionStatus { get; set; }
        public DateTimeOffset? SubscriptionExpiresAt { get; set; }

        // Profile summary (if onboarded)
        public bool HasProfile { get; set; }
        public string? SeekingType { get; set; }
        public List<string> TargetCountries { get; set; } = [];

        // Counts
        public int BookmarkCount { get; set; }
        public int ApplicationCount { get; set; }
        public int DocumentCount { get; set; }

        // Recent payments
        public List<AdminPaymentDto> RecentPayments { get; set; } = [];
    }

    public class AdminPaymentDto
    {
        public Guid Id { get; set; }
        public string TxRef { get; set; } = string.Empty;
        public string? ChapaReference { get; set; }
        public string Tier { get; set; } = string.Empty;
        public int AmountEtb { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    // Payment list can include the user it belongs to
    public class AdminPaymentListItemDto : AdminPaymentDto
    {
        public string UserId { get; set; } = string.Empty;
        public string? UserPhone { get; set; }
    }

    public class AdminUserFilterDto
    {
        public string? Search { get; set; }     // phone or name
        public int? Tier { get; set; }           // filter by SubscriptionTier
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class AdminPaymentFilterDto
    {
        public int? Status { get; set; }         // PaymentStatus
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

