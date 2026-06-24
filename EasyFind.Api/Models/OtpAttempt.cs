using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Models;

public class OtpAttempt
{
    [Key]
    public int OtpAttemptId { get; set; } 
    public string PhoneNumber { get; set; }
    public  DateTimeOffset CreatedAt { get; set; }
    public bool IsVerified { get; set; }
    public int FailedAttempts { get; set; }
}