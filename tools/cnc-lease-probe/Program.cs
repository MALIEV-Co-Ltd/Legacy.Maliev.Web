var mode = Environment.GetEnvironmentVariable("MALIEV_CNC_PROBE_MODE");
if (mode == "overflow")
{
    var block = new byte[4096];
    Array.Fill(block, (byte)'x');
    var stdout = Console.OpenStandardOutput();
    var stderr = Console.OpenStandardError();
    await Task.WhenAll(Task.Run(async () =>
    {
        for (var i = 0; i < 64; i++) await stdout.WriteAsync(block);
    }), Task.Run(async () =>
    {
        for (var i = 0; i < 64; i++) await stderr.WriteAsync(block);
    }));
}
else if (mode == "late-writer")
{
    await Task.Delay(250);
    await Console.Out.WriteAsync("late output without newline");
    await Console.Error.WriteAsync("late stderr without newline");
}
await Task.Delay(TimeSpan.FromMinutes(2));
