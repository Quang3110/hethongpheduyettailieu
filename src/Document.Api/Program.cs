using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();

var documents = new List<Document>
{
    new()
    {
        Id = Guid.NewGuid(),
        Title = "Quy trình phê duyệt tài liệu",
        CreatedBy = "system",
        CreatedAt = DateTime.UtcNow
    },
    new()
    {
        Id = Guid.NewGuid(),
        Title = "Biểu mẫu đề nghị phê duyệt",
        CreatedBy = "system",
        CreatedAt = DateTime.UtcNow
    }
};
var documentsLock = new object();

app.MapGet("/", () => Results.Ok(new
{
    service = "Document.Api",
    status = "running"
}));

app.MapGet("/documents", (string? status, string? reviewer) =>
{
    lock (documentsLock)
    {
        IEnumerable<Document> query = documents;

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusFilter = status.Trim();
            query = query.Where(d => string.Equals(d.Status, statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(reviewer))
        {
            var reviewerFilter = reviewer.Trim();
            query = query.Where(d => string.Equals(d.Reviewer, reviewerFilter, StringComparison.OrdinalIgnoreCase));
        }

        return Results.Ok(query.Select(ToSummary).ToList());
    }
});

app.MapGet("/documents/{id:guid}", (Guid id) =>
{
    lock (documentsLock)
    {
        var document = documents.FirstOrDefault(d => d.Id == id);

        return document is null
            ? DocumentNotFound(id)
            : Results.Ok(ToDetail(document));
    }
});

app.MapPost("/documents", (DocumentRequest? request) =>
{
    var errors = new List<FieldError>();

    if (string.IsNullOrWhiteSpace(request?.Title))
    {
        errors.Add(new FieldError("title", "title là bắt buộc và không được để trống."));
    }

    if (string.IsNullOrWhiteSpace(request?.CreatedBy))
    {
        errors.Add(new FieldError("createdBy", "createdBy là bắt buộc và không được để trống."));
    }

    if (errors.Count > 0)
    {
        return Results.BadRequest(new ErrorResponse(
            "INVALID_DOCUMENT",
            "Dữ liệu tài liệu không hợp lệ.",
            errors));
    }

    var document = new Document
    {
        Id = Guid.NewGuid(),
        Title = request!.Title!.Trim(),
        CreatedBy = request.CreatedBy!.Trim(),
        CreatedAt = DateTime.UtcNow
    };

    lock (documentsLock)
    {
        documents.Add(document);
    }

    return Results.Created($"/documents/{document.Id}", ToSummary(document));
});

app.MapPost("/documents/{id:guid}/submit", async (
    Guid id,
    SubmitRequest? request,
    IHubContext<DocumentHub> hubContext) =>
{
    DocumentDetailResponse detail;
    DocumentStatusChangedMessage message;

    lock (documentsLock)
    {
        var document = documents.FirstOrDefault(d => d.Id == id);
        if (document is null)
        {
            return DocumentNotFound(id);
        }

        if (string.IsNullOrWhiteSpace(request?.Reviewer))
        {
            return Results.BadRequest(new ErrorResponse(
                "INVALID_REVIEWER",
                "Dữ liệu gửi duyệt không hợp lệ.",
                new[] { new FieldError("reviewer", "reviewer là bắt buộc và không được để trống.") }));
        }

        if (document.Status != Statuses.Draft)
        {
            return InvalidTransition(document, "submit");
        }

        var previousStatus = document.Status;
        document.Status = Statuses.Submitted;
        document.Reviewer = request.Reviewer.Trim();

        var entry = new HistoryEntry(
            "submit",
            previousStatus,
            document.Status,
            document.CreatedBy,
            DateTime.UtcNow);
        document.History.Add(entry);

        detail = ToDetail(document);
        message = ToStatusMessage(document.Id, entry);
    }

    await PublishStatusChangedAsync(hubContext, message);

    return Results.Ok(detail);
});

app.MapPost("/documents/{id:guid}/approve", (Guid id, IHubContext<DocumentHub> hubContext) =>
    TransitionAsync(id, "approve", Statuses.Approved, hubContext));

app.MapPost("/documents/{id:guid}/reject", (Guid id, IHubContext<DocumentHub> hubContext) =>
    TransitionAsync(id, "reject", Statuses.Rejected, hubContext));

app.MapHub<DocumentHub>("/hubs/documents");

app.Run();

async Task<IResult> TransitionAsync(
    Guid id,
    string action,
    string targetStatus,
    IHubContext<DocumentHub> hubContext)
{
    DocumentDetailResponse detail;
    DocumentStatusChangedMessage message;

    lock (documentsLock)
    {
        var document = documents.FirstOrDefault(d => d.Id == id);
        if (document is null)
        {
            return DocumentNotFound(id);
        }

        if (document.Status != Statuses.Submitted)
        {
            return InvalidTransition(document, action);
        }

        var previousStatus = document.Status;
        document.Status = targetStatus;

        var entry = new HistoryEntry(
            action,
            previousStatus,
            targetStatus,
            document.Reviewer!,
            DateTime.UtcNow);
        document.History.Add(entry);

        detail = ToDetail(document);
        message = ToStatusMessage(document.Id, entry);
    }

    await PublishStatusChangedAsync(hubContext, message);

    return Results.Ok(detail);
}

async Task PublishStatusChangedAsync(
    IHubContext<DocumentHub> hubContext,
    DocumentStatusChangedMessage message)
{
    try
    {
        await hubContext.Clients
            .Group(DocumentGroups.For(message.DocumentId))
            .SendAsync("DocumentStatusChanged", message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(
            ex,
            "Không gửi được sự kiện DocumentStatusChanged cho tài liệu {DocumentId}.",
            message.DocumentId);
    }
}

static DocumentStatusChangedMessage ToStatusMessage(Guid documentId, HistoryEntry entry) =>
    new(documentId, entry.Action, entry.FromStatus, entry.ToStatus, entry.Actor, entry.Timestamp);

static IResult DocumentNotFound(Guid id) =>
    Results.NotFound(new ErrorResponse(
        "DOCUMENT_NOT_FOUND",
        $"Không tìm thấy tài liệu với id {id}."));

static IResult InvalidTransition(Document document, string action)
{
    var requiredStatus = action == "submit" ? Statuses.Draft : Statuses.Submitted;

    return Results.Conflict(new ErrorResponse(
        "INVALID_STATUS_TRANSITION",
        $"Không thể thực hiện '{action}' với tài liệu đang ở trạng thái '{document.Status}'. " +
        $"Thao tác này chỉ cho phép khi tài liệu ở trạng thái '{requiredStatus}'."));
}

static DocumentSummaryResponse ToSummary(Document document) =>
    new(document.Id, document.Title, document.CreatedBy, document.Status, document.CreatedAt);

static DocumentDetailResponse ToDetail(Document document) =>
    new(
        document.Id,
        document.Title,
        document.CreatedBy,
        document.Status,
        document.CreatedAt,
        document.Reviewer,
        document.History.ToList());

record DocumentRequest(string? Title, string? CreatedBy);

record SubmitRequest(string? Reviewer);

record DocumentSummaryResponse(Guid Id, string Title, string CreatedBy, string Status, DateTime CreatedAt);

record DocumentDetailResponse(
    Guid Id,
    string Title,
    string CreatedBy,
    string Status,
    DateTime CreatedAt,
    string? Reviewer,
    IReadOnlyList<HistoryEntry> History);

record HistoryEntry(string Action, string FromStatus, string ToStatus, string Actor, DateTime Timestamp);

record DocumentStatusChangedMessage(
    Guid DocumentId,
    string Action,
    string FromStatus,
    string ToStatus,
    string Actor,
    DateTime Timestamp);

record ErrorResponse(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<FieldError>? Errors = null);

record FieldError(string Field, string Message);

static class Statuses
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class DocumentGroups
{
    public static string For(Guid documentId) => $"document:{documentId:N}";
}

public sealed class DocumentHub : Hub
{
    public Task WatchDocument(Guid documentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, DocumentGroups.For(documentId));
}

sealed class Document
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string CreatedBy { get; init; } = string.Empty;

    public string Status { get; set; } = Statuses.Draft;

    public DateTime CreatedAt { get; init; }

    public string? Reviewer { get; set; }

    public List<HistoryEntry> History { get; } = [];
}
