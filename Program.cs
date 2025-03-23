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

// 转换方法
static void ProcessConversion(Action<string, string> conversionAction, string inputExtension, string outputExtension)
{
    Console.Write(string.Format(InputFilePathPrompt, inputExtension));
    string? inputPath = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(inputPath))
    {
        Console.WriteLine(string.Format(EmptyFilePathErrorMessage, inputExtension));
        return;
    }

    Console.Write(string.Format(OutputFilePathPrompt, outputExtension));
    string? outputPath = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(outputPath))
    {
        Console.WriteLine(string.Format(EmptyFilePathErrorMessage, outputExtension));
        return;
    }

    conversionAction(inputPath, outputPath);
}

// ASS -> QRC 
static void ConvertAssToQrc(string assPath, string qrcPath)
{
    try
    {
        Regex dialogueTimestampRegex = TimeRegex.DialogueTimestampRegex();
        Regex kTagRegex = TimeRegex.KTagRegex();

        using var reader = new StreamReader(assPath);
        using var writer = new StreamWriter(qrcPath);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!line.StartsWith("Dialogue:"))
                continue;

            // 提取时间戳
            Match timeMatch = dialogueTimestampRegex.Match(line);
            if (!timeMatch.Success)
                continue;

            int startMs = TimeToMilliseconds(timeMatch.Groups[1].Value);
            int endMs = TimeToMilliseconds(timeMatch.Groups[2].Value);
            int durationMs = endMs - startMs;

            // 提取文字及 K 标签
            MatchCollection kTagMatches = kTagRegex.Matches(line);
            var kValues = new List<int>();
            var words = new List<string>();

            foreach (Match match in kTagMatches)
            {
                kValues.Add(int.Parse(match.Groups[1].Value) * 10);
                words.Add(match.Groups[2].Value);
            }

            // 构造 QRC 格式
            var qrcLineBuilder = new StringBuilder();
            qrcLineBuilder.Append($"[{startMs},{durationMs}]");
            int currentTimestamp = startMs;
            for (int i = 0; i < words.Count; i++)
            {
                qrcLineBuilder.Append($"{words[i]}({currentTimestamp},{kValues[i]})");
                currentTimestamp += kValues[i];
            }

            writer.WriteLine(qrcLineBuilder.ToString());
        }
        Console.WriteLine(AssToQrcConversionComplete);
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

        using var reader = new StreamReader(qrcPath);
        using var writer = new StreamWriter(assPath);
        // 写入 ASS 头部信息
        writer.WriteLine("[Events]");
        writer.WriteLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
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

            string assText = assTextBuilder.ToString();
            string startTimeFormatted = MillisecondsToTime(startMs);
            string endTimeFormatted = MillisecondsToTime(endMs);
            string dialogueLine = $"Dialogue: 0,{startTimeFormatted},{endTimeFormatted},Default,,0,0,0,,{assText}";
            writer.WriteLine(dialogueLine);
        }
        Console.WriteLine(QrcToAssConversionComplete);
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
    return $"{h:D1}:{m:D2}:{s:D2}.{ms / 10:D2}";
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