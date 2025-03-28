using System.Text;
using System.Text.RegularExpressions;

const string AssToQrcChoice = "1";
const string QrcToAssChoice = "2";
const string AssExtension = ".ass";
const string QrcExtension = ".qrc";
const string InvalidChoiceMessage = "无效选择";
const string InputFilePathPrompt = "请输入 {0} 文件路径: ";
const string OutputFilePathPrompt = "请输入 {0} 文件路径: ";
const string EmptyFilePathErrorMessage = "输入的 {0} 文件路径不能为空";
const string AssToQrcConversionComplete = "ASS -> QRC 转换完成！";
const string QrcToAssConversionComplete = "QRC -> ASS 转换完成！";
const string ConversionErrorMessage = "转换过程中发生错误: {0}";

while (true)
{
    Console.WriteLine($"请选择操作：{AssToQrcChoice}. ASS -> QRC， {QrcToAssChoice}. QRC -> ASS");
    string? choice = Console.ReadLine()?.Trim();

    if (string.IsNullOrEmpty(choice))
    {
        Console.WriteLine(InvalidChoiceMessage);
        continue;
    }

    switch (choice)
    {
        case AssToQrcChoice:
            ProcessConversion(ConvertAssToQrc, AssExtension, QrcExtension);
            break;
        case QrcToAssChoice:
            ProcessConversion(ConvertQrcToAss, QrcExtension, AssExtension);
            break;
        default:
            Console.WriteLine(InvalidChoiceMessage);
            break;
    }
}

// 获取文件路径
static string ReadFilePath(string promptTemplate, string extension)
{
    while (true)
    {
        Console.Write(string.Format(promptTemplate, extension));
        string? path = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(path))
        {
            // 删除额外的引号
            if (path.StartsWith('\"') && path.EndsWith('\"'))
                path = path[1..^1];
            return path;
        }
        Console.WriteLine(string.Format(EmptyFilePathErrorMessage, extension));
    }
}

// 转换方法
static void ProcessConversion(Action<string, string> conversionAction, string inputExtension, string outputExtension)
{
    string inputPath = ReadFilePath(InputFilePathPrompt, inputExtension);
    string outputPath = ReadFilePath(OutputFilePathPrompt, outputExtension);
    conversionAction(inputPath, outputPath);
}

static void DisplayProgressBar(int current, int total)
{
    if (current >= total) current = total;
    int percentage = (int)((double)current / total * 100);
    int barLength = 20;
    int filledLength = (int)(barLength * percentage / 100.0);
    string bar = new string('=', filledLength) + new string(' ', barLength - filledLength);
    Console.Write($"\r[{bar}] {percentage}% ({current}/{total})");
}

// ASS -> QRC
static void ConvertAssToQrc(string assPath, string qrcPath)
{
    try
    {
        var dialogueTimestampRegex = TimeRegex.DialogueTimestampRegex();
        var kTagRegex = TimeRegex.KTagRegex();
        var fileInfo = new FileInfo(assPath);
        long totalBytes = fileInfo.Length;

        using var fs = new FileStream(assPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(fs);
        using var writer = new StreamWriter(qrcPath);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!line.StartsWith("Dialogue:"))
            {
                DisplayProgressBar((int)fs.Position, (int)totalBytes);
                continue;
            }
            var timeMatch = dialogueTimestampRegex.Match(line);
            if (!timeMatch.Success)
            {
                DisplayProgressBar((int)fs.Position, (int)totalBytes);
                continue;
            }
            int startMs = TimeToMilliseconds(timeMatch.Groups[1].Value);
            int endMs = TimeToMilliseconds(timeMatch.Groups[2].Value);
            int durationMs = endMs - startMs;

            var qrcLineBuilder = new StringBuilder();
            qrcLineBuilder.Append('[').Append(startMs).Append(',').Append(durationMs).Append(']');
            int currentTimestamp = startMs;
            foreach (Match match in kTagRegex.Matches(line))
            {
                int kValue = int.Parse(match.Groups[1].Value) * 10;
                qrcLineBuilder.Append(match.Groups[2].Value)
                              .Append('(')
                              .Append(currentTimestamp)
                              .Append(',')
                              .Append(kValue)
                              .Append(')');
                currentTimestamp += kValue;
            }
            writer.WriteLine(qrcLineBuilder);
            DisplayProgressBar((int)fs.Position, (int)totalBytes);
        }

        Console.WriteLine($"\n{AssToQrcConversionComplete}");
    }
    catch (Exception ex)
    {
        Console.WriteLine(string.Format(ConversionErrorMessage, ex.Message));
    }
}

// QRC -> ASS
static void ConvertQrcToAss(string qrcPath, string assPath)
{
    try
    {
        var qrcTimestampRegex = TimeRegex.QrcTimestampRegex();
        var wordTimeTagRegex = TimeRegex.WordTimeTagRegex();
        var fileInfo = new FileInfo(qrcPath);
        long totalBytes = fileInfo.Length;

        using var fs = new FileStream(qrcPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(fs);
        using var writer = new StreamWriter(assPath);
        // 写入 ASS 头部信息
        writer.WriteLine("[Events]");
        writer.WriteLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var headerMatch = qrcTimestampRegex.Match(line);
            if (!headerMatch.Success)
            {
                DisplayProgressBar((int)fs.Position, (int)totalBytes);
                continue;
            }

            int headerStart = int.Parse(headerMatch.Groups[1].Value);
            int durationMs = int.Parse(headerMatch.Groups[2].Value);
            int headerEnd = headerStart + durationMs;
            int headerEndIndex = line.IndexOf(']');
            if (headerEndIndex < 0)
            {
                DisplayProgressBar((int)fs.Position, (int)totalBytes);
                continue;
            }
            string segments = line[(headerEndIndex + 1)..];
            var matches = wordTimeTagRegex.Matches(segments);
            var assTextBuilder = new StringBuilder();
            // 处理 QRC 时间轴不连续的情况
            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                int segStart = int.Parse(m.Groups["start"].Value);
                int segDur = int.Parse(m.Groups["dur"].Value);
                int wordK = segDur / 10;
                string text = m.Groups["text"].Value.TrimEnd();
                assTextBuilder.Append($@"{{\k{wordK}}}{text}");

                int segEnd = segStart + segDur;
                int nextStart = i < matches.Count - 1 ? int.Parse(matches[i + 1].Groups["start"].Value) : headerEnd;
                if (nextStart > segEnd)
                    assTextBuilder.Append($@"{{\k{(nextStart - segEnd) / 10}}} ");
            }

            writer.WriteLine($"Dialogue: 0,{MillisecondsToTime(headerStart)},{MillisecondsToTime(headerEnd)},Default,,0,0,0,,{assTextBuilder}");
            DisplayProgressBar((int)fs.Position, (int)totalBytes);
        }

        Console.WriteLine($"\n{QrcToAssConversionComplete}");
    }
    catch (Exception ex)
    {
        Console.WriteLine(string.Format(ConversionErrorMessage, ex.Message));
    }
}

// 将时间戳转换为毫秒（ASS -> QRC）
static int TimeToMilliseconds(string time) => (((time[0] - '0') * 10 + (time[1] - '0')) * 3600 + ((time[3] - '0') * 10 + (time[4] - '0')) * 60 + (time[6] - '0') * 10 + (time[7] - '0')) * 1000 + ((time[9] - '0') * 10 + (time[10] - '0')) * 10;

// 将毫秒转换为时间戳（QRC -> ASS）
static string MillisecondsToTime(int ms) => $"{ms / 3600000:D2}:{ms % 3600000 / 60000:D2}:{(ms % 60000) / 1000:D2}.{(ms % 1000) / 10:D2}";

partial class TimeRegex
{
    // 匹配 ASS 行时间戳
    [GeneratedRegex(@"Dialogue:\s*\d+,(\d+:\d+:\d+\.\d+),(\d+:\d+:\d+\.\d+),")] 
    public static partial Regex DialogueTimestampRegex();
    // 匹配 ASS k tags
    [GeneratedRegex(@"\{\\k(\d+)\}([^\\{]*)")] 
    public static partial Regex KTagRegex();
    // 匹配 QRC 时间戳
    [GeneratedRegex(@"\[(\d+),(\d+)\]")] 
    public static partial Regex QrcTimestampRegex();
    // 匹配 QRC 词和时间
    [GeneratedRegex(@"(?<text>[^(]+)\((?<start>\d+),(?<dur>\d+)\)")] 
    public static partial Regex WordTimeTagRegex();
}