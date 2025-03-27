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
            var kTagMatches = kTagRegex.Matches(line);
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

        foreach (var line in allLines)
        {
            Match headerMatch = qrcTimestampRegex.Match(line);
            if (!headerMatch.Success) continue;

            int headerStart = int.Parse(headerMatch.Groups[1].Value);
            int durationMs = int.Parse(headerMatch.Groups[2].Value);
            int headerEnd = headerStart + durationMs;
            int headerEndIndex = line.IndexOf(']') + 1;
            string segmentsPart = line[headerEndIndex..];
            var matches = wordTimeTagRegex.Matches(segmentsPart);
            var assTextBuilder = new StringBuilder();
            // 处理 QRC 时间轴不连续的情况
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                int segStart = int.Parse(match.Groups["start"].Value);
                int segDur = int.Parse(match.Groups["dur"].Value);
                int wordK = segDur / 10;
                string fullText = match.Groups["text"].Value;
                string wordText = fullText.TrimEnd();
                string trailingSpaces = fullText[wordText.Length..];
                assTextBuilder.Append($@"{{\k{wordK}}}{wordText}");

                int segEnd = segStart + segDur;
                int nextStart = i < matches.Count - 1 ? int.Parse(matches[i + 1].Groups["start"].Value) : headerEnd;
                int gap = nextStart - segEnd;
                if (gap > 0)
                {
                    int gapK = gap / 10;
                    assTextBuilder.Append($@"{{\k{gapK}}} ");
                }
            }

            string startTimeFormatted = MillisecondsToTime(headerStart);
            string endTimeFormatted = MillisecondsToTime(headerEnd);
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
    var parts = time.Split([':', '.']).Select(int.Parse).ToArray();
    return (parts[0] * 3600 + parts[1] * 60 + parts[2]) * 1000 + parts[3] * 10;
}

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