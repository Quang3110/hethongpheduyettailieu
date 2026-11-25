using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

var apiUrl = args.Length > 0 ? args[0] : "http://localhost:5000";

if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var baseUri) ||
    (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
{
    Console.Error.WriteLine($"Địa chỉ API không hợp lệ: {apiUrl}");
    return 1;
}

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(10)
};

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

try
{
    Console.WriteLine($"API: {apiUrl}");
    Console.WriteLine();

    Console.WriteLine("=== 1) DANH SÁCH TÀI LIỆU BAN ĐẦU ===");
    var documents = await GetAsync<List<DocumentSummary>>("documents");
    PrintDocuments(documents);
    Console.WriteLine();

    Console.WriteLine("=== 2) TẠO TÀI LIỆU A ===");
    var createRequest = new CreateDocumentRequest("Tai lieu A - luong duyet", "alice");
    using var createResponse = await httpClient.PostAsJsonAsync("documents", createRequest);
    var createBody = await createResponse.Content.ReadAsStringAsync();

    if (createResponse.StatusCode != HttpStatusCode.Created)
    {
        throw new DemoException(
            $"POST /documents mong đợi HTTP 201 nhưng nhận {(int)createResponse.StatusCode}. Body: {createBody}");
    }

    var createdA = Deserialize<DocumentSummary>(createBody, "POST /documents");
    var location = createResponse.Headers.Location?.ToString();
    if (string.IsNullOrWhiteSpace(location))
    {
        throw new DemoException("Phản hồi 201 thiếu header Location.");
    }

    Console.WriteLine($"HTTP 201 Created | id={createdA.Id} | status={createdA.Status}");
    Console.WriteLine($"Location: {location}");

    var locatedA = await GetAsync<DocumentDetail>(location);
    if (locatedA.Id != createdA.Id)
    {
        throw new DemoException($"GET theo Location trả id {locatedA.Id}, mong đợi {createdA.Id}.");
    }

    if (locatedA.Status != "Draft")
    {
        throw new DemoException($"Tài liệu A có trạng thái {locatedA.Status}, mong đợi Draft.");
    }

    Console.WriteLine($"GET Location -> OK: id khớp, status={locatedA.Status}");
    Console.WriteLine();

    Console.WriteLine("=== 3) GỬI DUYỆT VÀ PHÊ DUYỆT TÀI LIỆU A ===");
    var submittedA = await PostAsync<DocumentDetail>(
        $"documents/{createdA.Id}/submit",
        new SubmitRequest("bob"));
    if (submittedA.Status != "Submitted" || submittedA.Reviewer != "bob")
    {
        throw new DemoException(
            $"Submit A trả status={submittedA.Status}, reviewer={submittedA.Reviewer}; mong đợi Submitted/bob.");
    }
    Console.WriteLine($"Submit -> HTTP 200 | status={submittedA.Status} | reviewer={submittedA.Reviewer}");

    var approvedA = await PostAsync<DocumentDetail>($"documents/{createdA.Id}/approve");
    if (approvedA.Status != "Approved")
    {
        throw new DemoException($"Approve A trả status={approvedA.Status}, mong đợi Approved.");
    }
    Console.WriteLine($"Approve -> HTTP 200 | status={approvedA.Status}");

    var detailA = await GetAsync<DocumentDetail>($"documents/{createdA.Id}");
    if (detailA.Status != "Approved")
    {
        throw new DemoException($"Chi tiết A có status={detailA.Status}, mong đợi Approved.");
    }
    if (detailA.History.Count != 2)
    {
        throw new DemoException($"Chi tiết A có {detailA.History.Count} mục lịch sử, mong đợi 2.");
    }

    Console.WriteLine($"Chi tiết A -> HTTP 200 | status={detailA.Status} | reviewer={detailA.Reviewer} | history={detailA.History.Count}");
    PrintHistory(detailA.History);
    Console.WriteLine();

    Console.WriteLine("=== 4) TẠO TÀI LIỆU B, GỬI DUYỆT VÀ TỪ CHỐI ===");
    var createdB = await PostAsync<DocumentSummary>(
        "documents",
        new CreateDocumentRequest("Tai lieu B - luong tu choi", "dave"));
    if (createdB.Status != "Draft")
    {
        throw new DemoException($"Tạo B trả status={createdB.Status}, mong đợi Draft.");
    }
    Console.WriteLine($"Tạo B -> HTTP 201 | id={createdB.Id} | status={createdB.Status}");

    var submittedB = await PostAsync<DocumentDetail>(
        $"documents/{createdB.Id}/submit",
        new SubmitRequest("carol"));
    if (submittedB.Status != "Submitted" || submittedB.Reviewer != "carol")
    {
        throw new DemoException(
            $"Submit B trả status={submittedB.Status}, reviewer={submittedB.Reviewer}; mong đợi Submitted/carol.");
    }
    Console.WriteLine($"Submit B -> HTTP 200 | status={submittedB.Status} | reviewer={submittedB.Reviewer}");

    var rejectedB = await PostAsync<DocumentDetail>($"documents/{createdB.Id}/reject");
    if (rejectedB.Status != "Rejected")
    {
        throw new DemoException($"Reject B trả status={rejectedB.Status}, mong đợi Rejected.");
    }
    Console.WriteLine($"Reject B -> HTTP 200 | status={rejectedB.Status}");

    var detailB = await GetAsync<DocumentDetail>($"documents/{createdB.Id}");
    if (detailB.Status != "Rejected")
    {
        throw new DemoException($"Chi tiết B có status={detailB.Status}, mong đợi Rejected.");
    }
    if (detailB.History.Count != 2)
    {
        throw new DemoException($"Chi tiết B có {detailB.History.Count} mục lịch sử, mong đợi 2.");
    }

    Console.WriteLine($"Chi tiết B -> HTTP 200 | status={detailB.Status} | reviewer={detailB.Reviewer} | history={detailB.History.Count}");
    PrintHistory(detailB.History);
    Console.WriteLine();

    Console.WriteLine("=== 5) LỌC status=Approved&reviewer=bob ===");
    var filtered = await GetAsync<List<DocumentSummary>>("documents?status=Approved&reviewer=bob");
    PrintDocuments(filtered);
    if (!filtered.Any(d => d.Id == createdA.Id))
    {
        throw new DemoException("Danh sách lọc không chứa ID của tài liệu A.");
    }
    Console.WriteLine("OK: danh sách lọc chứa đúng tài liệu A.");
    Console.WriteLine();

    Console.WriteLine("=== 6) CÁC YÊU CẦU LỖI CÓ CHỦ ĐÍCH ===");

    Console.WriteLine("- POST /documents với title rỗng:");
    using (var invalidCreate = await httpClient.PostAsJsonAsync(
               "documents",
               new CreateDocumentRequest("   ", "alice")))
    {
        var body = await invalidCreate.Content.ReadAsStringAsync();
        ExpectError(
            "POST /documents (title rỗng)",
            invalidCreate.StatusCode,
            body,
            HttpStatusCode.BadRequest,
            "INVALID_DOCUMENT");
    }
    Console.WriteLine();

    var missingId = Guid.NewGuid();
    Console.WriteLine($"- GET /documents/{missingId} (GUID không tồn tại):");
    using (var notFound = await httpClient.GetAsync($"documents/{missingId}"))
    {
        var body = await notFound.Content.ReadAsStringAsync();
        ExpectError(
            "GET /documents/{id} (không tồn tại)",
            notFound.StatusCode,
            body,
            HttpStatusCode.NotFound,
            "DOCUMENT_NOT_FOUND");
    }
    Console.WriteLine();

    Console.WriteLine("- POST /documents/{id}/approve lặp lại trên tài liệu A đã Approved:");
    using (var conflict = await httpClient.PostAsync($"documents/{createdA.Id}/approve", null))
    {
        var body = await conflict.Content.ReadAsStringAsync();
        ExpectError(
            "Approve lại tài liệu A",
            conflict.StatusCode,
            body,
            HttpStatusCode.Conflict,
            "INVALID_STATUS_TRANSITION");
    }
    Console.WriteLine();

    Console.WriteLine("=== DEMO HOÀN TẤT: TẤT CẢ KIỂM TRA ĐỀU ĐẠT ===");
    return 0;
}
catch (DemoException ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"DEMO THẤT BẠI: {ex.Message}");
    return 1;
}
catch (HttpRequestException ex) when (ex.StatusCode is not null)
{
    Console.Error.WriteLine($"API trả lỗi HTTP không mong đợi: {(int)ex.StatusCode} {ex.Message}");
    return 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine(
        $"Không kết nối được API tại {apiUrl}. Hãy kiểm tra API đã chạy và địa chỉ có đúng không. Chi tiết: {ex.Message}");
    return 1;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine($"Yêu cầu tới API tại {apiUrl} đã quá thời gian chờ.");
    return 1;
}

async Task<T> GetAsync<T>(string path)
{
    using var response = await httpClient.GetAsync(path);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        throw new DemoException($"GET {path} trả HTTP {(int)response.StatusCode}. Body: {body}");
    }

    return Deserialize<T>(body, $"GET {path}");
}

async Task<T> PostAsync<T>(string path, object? payload = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path);
    if (payload is not null)
    {
        request.Content = JsonContent.Create(payload);
    }

    using var response = await httpClient.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        throw new DemoException($"POST {path} trả HTTP {(int)response.StatusCode}. Body: {body}");
    }

    return Deserialize<T>(body, $"POST {path}");
}

T Deserialize<T>(string body, string context)
{
    try
    {
        var value = JsonSerializer.Deserialize<T>(body, jsonOptions);
        if (value is null)
        {
            throw new DemoException($"{context}: phản hồi JSON rỗng hoặc không đọc được.");
        }

        return value;
    }
    catch (JsonException ex)
    {
        throw new DemoException($"{context}: JSON không hợp lệ. {ex.Message}");
    }
}

void ExpectError(
    string label,
    HttpStatusCode actualStatus,
    string body,
    HttpStatusCode expectedStatus,
    string expectedCode)
{
    ErrorResponse? error = null;
    try
    {
        error = JsonSerializer.Deserialize<ErrorResponse>(body, jsonOptions);
    }
    catch (JsonException)
    {
        // Giữ error = null để báo lỗi bên dưới.
    }

    Console.WriteLine($"  HTTP thực tế: {(int)actualStatus} {actualStatus}");
    Console.WriteLine($"  code: {error?.Code ?? "(không đọc được)"}");
    Console.WriteLine($"  message: {error?.Message ?? body}");

    if (error?.Errors is { Count: > 0 })
    {
        foreach (var fieldError in error.Errors)
        {
            Console.WriteLine($"  lỗi trường {fieldError.Field}: {fieldError.Message}");
        }
    }

    if (actualStatus != expectedStatus || error?.Code != expectedCode)
    {
        throw new DemoException(
            $"{label}: mong đợi {(int)expectedStatus} {expectedStatus}/{expectedCode}, " +
            $"nhận {(int)actualStatus} {actualStatus}/{error?.Code ?? "?"}.");
    }

    Console.WriteLine("  -> đúng như mong đợi.");
}

void PrintDocuments(IReadOnlyList<DocumentSummary> items)
{
    Console.WriteLine($"Số tài liệu: {items.Count}");
    foreach (var document in items)
    {
        Console.WriteLine(
            $"  - {document.Id} | {document.Title} | người tạo: {document.CreatedBy} | trạng thái: {document.Status}");
    }
}

void PrintHistory(IReadOnlyList<HistoryEntry> history)
{
    Console.WriteLine($"Lịch sử ({history.Count} mục):");
    foreach (var entry in history)
    {
        Console.WriteLine(
            $"  - {entry.Action} | {entry.FromStatus} -> {entry.ToStatus} | người xử lý: {entry.Actor} | {entry.Timestamp:O}");
    }
}

record CreateDocumentRequest(string Title, string CreatedBy);

record SubmitRequest(string Reviewer);

record DocumentSummary(Guid Id, string Title, string CreatedBy, string Status, DateTime CreatedAt);

record DocumentDetail(
    Guid Id,
    string Title,
    string CreatedBy,
    string Status,
    DateTime CreatedAt,
    string? Reviewer,
    List<HistoryEntry> History);

record HistoryEntry(string Action, string FromStatus, string ToStatus, string Actor, DateTime Timestamp);

record ErrorResponse(string Code, string Message, List<FieldError>? Errors);

record FieldError(string Field, string Message);

sealed class DemoException(string message) : Exception(message);
