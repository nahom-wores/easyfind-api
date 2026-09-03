namespace EasyFind.Api.Models.Options;

// Tunable without touching code. Defaults are deliberately conservative: an OTP
// send costs real money and lands on someone's phone.
public class OtpThrottleOptions
{
    public const string SectionName = "OtpThrottle";

    // ── Sending ──────────────────────────────────────────────────────────
    // Per destination phone number. Stops one number being SMS-bombed.
    public int SendsPerWindow { get; set; } = 3;
    public int SendWindowMinutes { get; set; } = 60;

    // ── Verifying ────────────────────────────────────────────────────────
    // Consecutive wrong codes before the number is locked out. Guards against
    // brute-forcing a 6-digit code.
    public int MaxFailedVerifications { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;

    // ── Per-IP backstop (middleware, in-memory per instance) ─────────────
    // Deliberately looser than the per-phone limits: an attacker rotating phone
    // numbers is what this catches, and several real users can share one NAT IP.
    public int SendsPerIpPerWindow { get; set; } = 10;
    public int VerificationsPerIpPerWindow { get; set; } = 20;
    public int IpWindowMinutes { get; set; } = 15;

    public TimeSpan SendWindow => TimeSpan.FromMinutes(SendWindowMinutes);
    public TimeSpan Lockout => TimeSpan.FromMinutes(LockoutMinutes);
    public TimeSpan IpWindow => TimeSpan.FromMinutes(IpWindowMinutes);
}
