using System.Text;
using System.Text.RegularExpressions;

while (true)
{
    Console.WriteLine("请选择操作：1. ASS -> QRC， 2. QRC -> ASS");
    string? choice = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(choice))
    {
        Console.WriteLine("无效选择");
        continue;
    }

    switch (choice)
    {
        case "1":
            ProcessConversion(ConvertAssToQrc, ".ass", ".qrc");
            break;
        case "2":
            ProcessConversion(ConvertQrcToAss, ".qrc", ".ass");
            break;
        default:
            Console.WriteLine("无效选择");
            break;
    }
}

static void ProcessConversion(Action<string, string> conversionAction, string inputExtension, string outputExtension)
{
    Console.Write($"输入 {inputExtension} 文件路径: ");
    string? inputPath = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(inputPath))
    {
        Console.WriteLine($"输入的 {inputExtension} 文件路径不能为空");
        return;
    }
    Console.Write($"输出 {outputExtension} 文件路径: ");
    string? outputPath = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(outputPath))
    {
        Console.WriteLine($"输出的 {outputExtension} 文件路径不能为空");
        return;
    }
    conversionAction(inputPath, outputPath);
}

// ASS --> QRC
void ConvertAssToQrc(string assPath, string qrcPath)
{
    try
    {
        Regex dialogueTimestampRegex = DialogueTimestampRegex();
        Regex kTagRegex = KTagRegex();
        using (StreamReader reader = new(assPath))
        using (StreamWriter writer = new(qrcPath))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith("Dialogue:")) continue;

                // 提取时间戳
                Match timeMatch = dialogueTimestampRegex.Match(line);
                if (!timeMatch.Success) continue;

                int start = TimeToMilliseconds(timeMatch.Groups[1].Value);
                int end = TimeToMilliseconds(timeMatch.Groups[2].Value);
                int duration = end - start;

                // 提取文字
                MatchCollection kTags = kTagRegex.Matches(line);
                List<int> kValues = [];
                List<string> words = [];

                foreach (Match match in kTags)
                {
                    kValues.Add(int.Parse(match.Groups[1].Value) * 10);
                    words.Add(match.Groups[2].Value);
                }

                // 构造 QRC 格式
                StringBuilder sb = new();
                sb.Append($"[{start},{duration}]");
                int currentTime = start;
                for (int i = 0; i < words.Count; i++)
                {
                    sb.Append($"{words[i]}({currentTime},{kValues[i]})");
                    currentTime += kValues[i];
                }

                writer.WriteLine(sb.ToString());
            }
        }
        Console.WriteLine("ASS -> QRC 转换完成！");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"转换过程中发生错误: {ex.Message}");
    }
}

// QRC --> ASS
void ConvertQrcToAss(string qrcPath, string assPath)
{
    try
    {
        Regex qrcTimestampRegex = QrcTimestampRegex();
        Regex wordTimeTagRegex = WordTimeTagRegex();

        using (StreamReader reader = new(qrcPath))
        using (StreamWriter writer = new(assPath))
        {
            writer.WriteLine("[Events]");
            writer.WriteLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // 提取[start,duration]
                Match headerMatch = qrcTimestampRegex.Match(line);
                if (!headerMatch.Success) continue;

                int start = int.Parse(headerMatch.Groups[1].Value);
                int duration = int.Parse(headerMatch.Groups[2].Value);
                int end = start + duration;

                // 去掉头部内容，得到后面的段落部分
                int headerEnd = line.IndexOf(']') + 1;
                string segmentsPart = line[headerEnd..];

                // 匹配时间标签
                MatchCollection matches = wordTimeTagRegex.Matches(segmentsPart);
                int lastIndex = 0;
                StringBuilder assTextBuilder = new();

                foreach (Match m in matches)
                {
                    string word = segmentsPart[lastIndex..m.Index];
                    int kValue = int.Parse(m.Groups[2].Value) / 10;
                    assTextBuilder.Append($@"{{\k{kValue}}}{word}");
                    lastIndex = m.Index + m.Length;
                }
                assTextBuilder.Append(segmentsPart[lastIndex..]);
                string assText = assTextBuilder.ToString();
                string startFormatted = MillisecondsToTime(start);
                string endFormatted = MillisecondsToTime(end);
                string dialogueLine = $"Dialogue: 0,{startFormatted},{endFormatted},Default,,0,0,0,,{assText}";
                writer.WriteLine(dialogueLine);
            }
        }
        Console.WriteLine("QRC -> ASS 转换完成！");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"转换过程中发生错误: {ex.Message}");
    }
}

// ASS --> QRC 时间戳
int TimeToMilliseconds(string time)
{
    var parts = time.Split([':', '.']);
    int h = int.Parse(parts[0]);
    int m = int.Parse(parts[1]);
    int s = int.Parse(parts[2]);
    int ms = int.Parse(parts[3]);
    return (h * 3600 + m * 60 + s) * 1000 + ms * 10;
}

// QRC --> ASS 时间戳
string MillisecondsToTime(int ms)
{
    int h = ms / 3600000;
    ms %= 3600000;
    int m = ms / 60000;
    ms %= 60000;
    int s = ms / 1000;
    ms %= 1000;
    return $"{h:D1}:{m:D2}:{s:D2}.{ms / 10:D2}";
}

partial class Program
{
    [GeneratedRegex(@"Dialogue:\s*\d+,(\d+:\d+:\d+\.\d+),(\d+:\d+:\d+\.\d+),")]
    private static partial Regex DialogueTimestampRegex();
    [GeneratedRegex(@"\{\\k(\d+)\}([^\\{]*)")]
    private static partial Regex KTagRegex();
    [GeneratedRegex(@"\[(\d+),(\d+)\]")]
    private static partial Regex QrcTimestampRegex();
    [GeneratedRegex(@"\((\d+),(\d+)\)")]
    private static partial Regex WordTimeTagRegex();
}