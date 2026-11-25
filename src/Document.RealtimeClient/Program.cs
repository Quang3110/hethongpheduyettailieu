using System.Text;
using Microsoft.AspNetCore.SignalR.Client;

Console.OutputEncoding = Encoding.UTF8;

const string DefaultApiUrl = "http://localhost:5000";

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

if (!Guid.TryParse(args[0], out var documentId))
{
    Console.Error.WriteLine($"GUID tài liệu không hợp lệ: {args[0]}");
    Console.WriteLine();
    PrintUsage();
    return 1;
}

var apiUrl = args.Length > 1 ? args[1] : DefaultApiUrl;

if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var apiUri) ||
    (apiUri.Scheme != Uri.UriSchemeHttp && apiUri.Scheme != Uri.UriSchemeHttps))
{
    Console.Error.WriteLine($"Địa chỉ API không hợp lệ: {apiUrl}");
    Console.WriteLine();
    PrintUsage();
    return 1;
}

var hubUrl = new Uri(new Uri(apiUrl.TrimEnd('/') + "/"), "hubs/documents");

var connection = new HubConnectionBuilder()
    .WithUrl(hubUrl)
    .WithAutomaticReconnect()
    .Build();

connection.On<DocumentStatusChangedMessage>("DocumentStatusChanged", message =>
{
    var mismatch = message.DocumentId != documentId
        ? " [CẢNH BÁO: sự kiện thuộc tài liệu KHÁC đang theo dõi]"
        : string.Empty;

    Console.WriteLine(
        $"[EVENT] documentId={message.DocumentId} | action={message.Action} | " +
        $"{message.FromStatus} -> {message.ToStatus} | actor={message.Actor} | " +
        $"timestamp={message.Timestamp:O}{mismatch}");
});

connection.Reconnecting += error =>
{
    Console.WriteLine($"[HUB] Mất kết nối ({error?.Message ?? "không rõ lý do"}), đang thử kết nối lại...");
    return Task.CompletedTask;
};

connection.Reconnected += async _ =>
{
    try
    {
        await connection.InvokeAsync("WatchDocument", documentId);
        Console.WriteLine($"[HUB] Đã kết nối lại và đăng ký lại theo dõi tài liệu {documentId}.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"[HUB] Kết nối lại thành công nhưng KHÔNG đăng ký lại được WatchDocument cho tài liệu {documentId}: {ex.Message}");
    }
};

connection.Closed += error =>
{
    Console.WriteLine($"[HUB] Kết nối đã đóng. {error?.Message}");
    return Task.CompletedTask;
};

try
{
    await connection.StartAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Không thể kết nối tới hub tại {hubUrl}: {ex.Message}");
    await connection.DisposeAsync();
    return 1;
}

try
{
    await connection.InvokeAsync("WatchDocument", documentId);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Không đăng ký được theo dõi tài liệu {documentId}: {ex.Message}");
    await connection.DisposeAsync();
    return 1;
}

Console.WriteLine($"Đang theo dõi tài liệu {documentId}");
Console.WriteLine($"Hub: {hubUrl}");
Console.WriteLine("Nhấn Ctrl+C để thoát.");

var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};

await stopped.Task;

Console.WriteLine("Đang ngắt kết nối...");
await connection.StopAsync();
await connection.DisposeAsync();
Console.WriteLine("Đã ngắt kết nối.");

return 0;

void PrintUsage()
{
    Console.WriteLine("Sử dụng: Document.RealtimeClient <documentId> [apiUrl]");
    Console.WriteLine();
    Console.WriteLine("  documentId  GUID của tài liệu cần theo dõi (bắt buộc).");
    Console.WriteLine($"  apiUrl      Địa chỉ API, mặc định {DefaultApiUrl}.");
    Console.WriteLine();
    Console.WriteLine("Ví dụ: Document.RealtimeClient 3f2504e0-4f89-11d3-9a0c-0305e82c3301");
}

record DocumentStatusChangedMessage(
    Guid DocumentId,
    string Action,
    string FromStatus,
    string ToStatus,
    string Actor,
    DateTime Timestamp);
