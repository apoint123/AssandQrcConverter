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
static string? ReadFilePath(string promptTemplate, string extension)
{
    Console.Write(string.Format(promptTemplate, extension));
    string? path = Console.ReadLine()?.Trim();
    return string.IsNullOrEmpty(path) ? null : path;
}

// 转换方法
static void ProcessConversion(Action<string, string> conversionAction, string inputExtension, string outputExtension)
{
    string? inputPath = ReadFilePath(InputFilePathPrompt, inputExtension);
    if (inputPath is null)
    {
        Console.WriteLine(string.Format(EmptyFilePathErrorMessage, inputExtension));
        return;
    }

    string? outputPath = ReadFilePath(OutputFilePathPrompt, outputExtension);
    if (outputPath is null)
    {
        Console.WriteLine(string.Format(EmptyFilePathErrorMessage, outputExtension));
        return;
    }

    conversionAction(inputPath, outputPath);
}

static void DisplayProgressBar(int current, int total)
{
    int percentage = (int)(((double)current / total) * 100);
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
        Regex dialogueTimestampRegex = TimeRegex.DialogueTimestampRegex();
        Regex kTagRegex = TimeRegex.KTagRegex();

        var dialogueLines = File.ReadLines(assPath).Where(line => line.StartsWith("Dialogue:")).ToList();
        int totalDialogueLines = dialogueLines.Count;
        int processedLines = 0;
        using var writer = new StreamWriter(qrcPath);
        foreach (var line in dialogueLines)
        {
            // 提取时间戳
            Match timeMatch = dialogueTimestampRegex.Match(line);
            if (!timeMatch.Success)
                continue;

            int startMs = TimeToMilliseconds(timeMatch.Groups[1].Value);
            int endMs = TimeToMilliseconds(timeMatch.Groups[2].Value);
            int durationMs = endMs - startMs;

            // 提取文字及 K 标签
            MatchCollection kTagMatches = kTagRegex.Matches(line);
            var kTagValues = new List<int>();
            var words = new List<string>();

            foreach (Match match in kTagMatches)
            {
                kTagValues.Add(int.Parse(match.Groups[1].Value) * 10);
                words.Add(match.Groups[2].Value);
            }

            // 构造 QRC 格式
            var qrcLineBuilder = new StringBuilder();
            qrcLineBuilder.Append($"[{startMs},{durationMs}]");
            int currentTimestamp = startMs;
            for (int i = 0; i < words.Count; i++)
            {
                qrcLineBuilder.Append($"{words[i]}({currentTimestamp},{kTagValues[i]})");
                currentTimestamp += kTagValues[i];
            }

            writer.WriteLine(qrcLineBuilder.ToString());
            processedLines++;
            DisplayProgressBar(processedLines, totalDialogueLines);
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
        Regex qrcTimestampRegex = TimeRegex.QrcTimestampRegex();
        Regex wordTimeTagRegex = TimeRegex.WordTimeTagRegex();

        var allLines = File.ReadLines(qrcPath).ToList();
        int totalLines = allLines.Count;
        int processedLines = 0;
        using var writer = new StreamWriter(assPath);
        // 写入 ASS 头部信息
        writer.WriteLine("[Events]");
        writer.WriteLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        for (int i = 0; i < totalLines; i++)
        {
            string? line = allLines[i];
            // 提取 [start,duration]
            Match headerMatch = qrcTimestampRegex.Match(line);
            if (!headerMatch.Success)
                continue;

            int startMs = int.Parse(headerMatch.Groups[1].Value);
            int durationMs = int.Parse(headerMatch.Groups[2].Value);
            int endMs = startMs + durationMs;

            // 剔除头部，获取剩余内容
            int headerEndIndex = line.IndexOf(']') + 1;
            string segmentsPart = line[headerEndIndex..];

            // 根据时间标签生成 ASS 格式文本
            MatchCollection matches = wordTimeTagRegex.Matches(segmentsPart);
            int lastIndex = 0;
            var assTextBuilder = new StringBuilder();

            foreach (Match match in matches)
            {
                string wordSegment = segmentsPart[lastIndex..match.Index];
                int kValue = int.Parse(match.Groups[2].Value) / 10;
                assTextBuilder.Append($@"{{\k{kValue}}}{wordSegment}");
                lastIndex = match.Index + match.Length;
            }
            assTextBuilder.Append(segmentsPart[lastIndex..]);

            string startTimeFormatted = MillisecondsToTime(startMs);
            string endTimeFormatted = MillisecondsToTime(endMs);
            writer.WriteLine($"Dialogue: 0,{startTimeFormatted},{endTimeFormatted},Default,,0,0,0,,{assTextBuilder}");
            processedLines++;
            DisplayProgressBar(processedLines, totalLines);
        }

        Console.WriteLine($"\n{QrcToAssConversionComplete}");
    }
    catch (Exception ex)
    {
        Console.WriteLine(string.Format(ConversionErrorMessage, ex.Message));
    }
}

// 将时间戳转换为毫秒（ASS -> QRC）
static int TimeToMilliseconds(string time)
{
    var parts = time.Split([':', '.']);
    int h = int.Parse(parts[0]);
    int m = int.Parse(parts[1]);
    int s = int.Parse(parts[2]);
    int ms = int.Parse(parts[3]);
    return (h * 3600 + m * 60 + s) * 1000 + ms * 10;
}

// 将毫秒转换为时间戳（QRC -> ASS）
static string MillisecondsToTime(int ms)
{
    int h = ms / 3600000;
    ms %= 3600000;
    int m = ms / 60000;
    ms %= 60000;
    int s = ms / 1000;
    ms %= 1000;
    return $"{h}:{m:D2}:{s:D2}.{ms / 10:D2}";
}

// 时间戳和文字的正则表达式
partial class TimeRegex
{
    [GeneratedRegex(@"Dialogue:\s*\d+,(\d+:\d+:\d+\.\d+),(\d+:\d+:\d+\.\d+),")]
    public static partial Regex DialogueTimestampRegex();

    [GeneratedRegex(@"\{\\k(\d+)\}([^\\{]*)")]
    public static partial Regex KTagRegex();

    [GeneratedRegex(@"\[(\d+),(\d+)\]")]
    public static partial Regex QrcTimestampRegex();

    [GeneratedRegex(@"\((\d+),(\d+)\)")]
    public static partial Regex WordTimeTagRegex();
}