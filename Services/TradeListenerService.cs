using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GameHelper.Services;

/// <summary>Локальный HTTP-сервер на localhost:7123 для приёма trade-данных из Tampermonkey.</summary>
public sealed class TradeListenerService : IDisposable
{
    private const string Prefix = "http://localhost:7123/";

    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }

    /// <summary>Срабатывает на UI-потоке (через Dispatcher вызывающей стороны).
    /// Возвращает строку статуса для отображения.</summary>
    public event Func<string, string>? OnJsonReceived;

    public void Start()
    {
        if (IsRunning) return;
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        Task.Run(() => AcceptLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        _listener.Stop();
        _listener.Prefixes.Clear();
        IsRunning = false;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }

            _ = Task.Run(() => Handle(ctx), ct);
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;

        res.Headers["Access-Control-Allow-Origin"] = "*";
        res.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        res.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Slug";

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = 204;
            res.Close();
            return;
        }

        if (req.HttpMethod != "POST" || !req.Url!.AbsolutePath.StartsWith("/trade-import"))
        {
            res.StatusCode = 404;
            res.Close();
            return;
        }

        try
        {
            string body;
            using (var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                body = sr.ReadToEnd();

            var status = OnJsonReceived?.Invoke(body) ?? "нет обработчика";

            var json = JsonSerializer.Serialize(new { ok = true, status });
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentType = "application/json";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes);
        }
        catch (Exception ex)
        {
            var err = Encoding.UTF8.GetBytes($"{{\"ok\":false,\"error\":\"{ex.Message}\"}}");
            res.StatusCode = 500;
            res.OutputStream.Write(err);
        }
        finally
        {
            res.Close();
        }
    }
}
