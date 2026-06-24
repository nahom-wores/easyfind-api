using System.ComponentModel.DataAnnotations;
using EasyFind.Api.Models.Auth;

namespace EasyFind.Api.Models;

public class Notification 
{
    [Key]
    public int Id { get; set; }
    public string UserId { get; set; }
    public ApplicationUser User { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
    public string Type { get; set; }
    public string? ActionUrl { get; set; } // "/messages/5"
    public string? ImageUrl { get; set; }  // optional avatar/icon
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}