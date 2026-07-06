using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Users;
using EasyFind.Api.Services.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class DocumentsController(IDocumentService documentService) : ApiControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    
    [HttpPost("upload")]
    [RequestSizeLimit(6 * 1024 * 1024)]   // 6MB ceiling at the framework level
    public async Task<ActionResult<ApiResponse>> Upload(
        IFormFile file, [FromForm] DocumentType type, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        if (file is null) return HandleResult(
            Result<object>.Validation("No file provided."));

        var result = await documentService.UploadAsync(UserId, file, type, ct);
        return HandleResult(result);
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMyDocuments(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await documentService.GetUserDocumentsAsync(UserId, ct);
        return HandleResult(result);
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<ActionResult<ApiResponse>> GetDownloadUrl(Guid documentId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await documentService.GetDownloadUrlAsync(UserId, documentId, ct);
        return HandleResult(result);
    }

    [HttpDelete("{documentId:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid documentId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var result = await documentService.DeleteAsync(UserId, documentId, ct);
        return HandleResult(result, "Document deleted.");
    }
}