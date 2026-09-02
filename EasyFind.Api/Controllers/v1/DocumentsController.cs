using System.Security.Claims;
using Asp.Versioning;
using EasyFind.Api.Features.Documents.Commands;
using EasyFind.Api.Features.Documents.Queries;
using EasyFind.Api.Models.Dto.Common;
using EasyFind.Api.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyFind.Api.Controllers.v1;

// CVs and supporting documents. Files are private: listing them never exposes a
// storage key, and downloading goes through a time-limited URL.
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[ApiVersion("1.0")]
[Authorize]
public class DocumentsController : ApiControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(6 * 1024 * 1024)]   // 6MB ceiling at the framework level
    public async Task<ActionResult<ApiResponse>> Upload(
        IFormFile file,
        [FromForm] DocumentType type,
        [FromServices] UploadDocumentHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (file is null) return HandleResult(Result<object>.Validation("No file provided."));

        return HandleResult(await handler.HandleAsync(userId, file, type, ct));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse>> GetMyDocuments(
        [FromServices] GetUserDocumentsHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(userId, ct));
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<ActionResult<ApiResponse>> GetDownloadUrl(
        Guid documentId,
        [FromServices] GetDocumentDownloadUrlHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(userId, documentId, ct));
    }

    [HttpDelete("{documentId:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(
        Guid documentId,
        [FromServices] DeleteDocumentHandler handler,
        CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return HandleResult(await handler.HandleAsync(userId, documentId, ct), "Document deleted.");
    }
}
