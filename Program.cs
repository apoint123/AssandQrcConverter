using System.Text.RegularExpressions;


Console.WriteLine("请选择操作：1. ASS -> QRC， 2. QRC -> ASS");
string? choice = Console.ReadLine();
if (string.IsNullOrWhiteSpace(choice))
{
    Console.WriteLine("无效选择");
    return;
}
string trimmedChoice = choice.Trim();
if (trimmedChoice == "1")
{
    Console.Write("输入 .ass 文件路径: ");
    string? assPath = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(assPath))
    {
        Console.WriteLine("输入的 ASS 文件路径不能为空");
        return;
    }

    Console.Write("输出 .qrc 文件路径: ");
    string? qrcPath = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(qrcPath))
    {
        Console.WriteLine("输出的 QRC 文件路径不能为空");
        return;
    }

    ConvertAssToQrc(assPath, qrcPath);
}
else if (trimmedChoice == "2")
{
    Console.Write("输入 .qrc 文件路径: ");
    string? qrcPath = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(qrcPath))
    {
        Console.WriteLine("输入的 QRC 文件路径不能为空");
        return;
    }

    Console.Write("输出 .ass 文件路径: ");
    string? assPath = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(assPath))
    {
        Console.WriteLine("输出的 ASS 文件路径不能为空");
        return;
    }

    ConvertQrcToAss(qrcPath, assPath); 
}
else
{
    Console.WriteLine("无效选择");
}

// ASS --> QRC
void ConvertAssToQrc(string assPath, string qrcPath)
{
    var lines = File.ReadAllLines(assPath);
    List<string> output = [];

    foreach (var line in lines)
    {
        if (!line.StartsWith("Dialogue:")) continue;

        // 提取时间戳
        Match timeMatch = DialogueTimestampRegex().Match(line);
        if (!timeMatch.Success) continue;

        int start = TimeToMilliseconds(timeMatch.Groups[1].Value);
        int end = TimeToMilliseconds(timeMatch.Groups[2].Value);
        int duration = end - start;

        // 提取文字
        MatchCollection kTags = KTagRegex().Matches(line);
        List<int> kValues = [];
        List<string> words = [];

        foreach (Match match in kTags)
        {
            kValues.Add(int.Parse(match.Groups[1].Value) * 10);
            words.Add(match.Groups[2].Value);
        }

        // 构造 QRC 格式
        List<string> wordEntries = [];
        int currentTime = start;
        for (int i = 0; i < words.Count; i++)
        {
            wordEntries.Add($"{words[i]}({currentTime},{kValues[i]})");
            currentTime += kValues[i];
        }

        output.Add($"[{start},{duration}]{string.Join("", wordEntries)}");
    }

    File.WriteAllLines(qrcPath, output);
    Console.WriteLine("ASS -> QRC 转换完成！");
}

// QRC --> ASS
void ConvertQrcToAss(string qrcPath, string assPath)
{
    var lines = File.ReadAllLines(qrcPath);
    List<string> output =
        [
            "[Events]",
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text"
        ];

    foreach (var line in lines)
    {
        // 提取[start,duration]
        Match headerMatch = QrcTimestampRegex().Match(line);
        if (!headerMatch.Success) continue;
        int start = int.Parse(headerMatch.Groups[1].Value);
        int duration = int.Parse(headerMatch.Groups[2].Value);
        int end = start + duration;
        // 去掉头部内容，得到后面的段落部分
        int headerEnd = line.IndexOf(']') + 1;
        string segmentsPart = line[headerEnd..];
        // 匹配时间标签
        Regex timeTagRegex = WordTimeTagRegex();
        var matches = timeTagRegex.Matches(segmentsPart);
        int lastIndex = 0;
        List<string> assSegments = [];
        foreach (Match m in matches)
        {
            string word = segmentsPart[lastIndex..m.Index];
            int kValue = int.Parse(m.Groups[2].Value) / 10;
            assSegments.Add($@"{{\k{kValue}}}{word}");
            lastIndex = m.Index + m.Length;
        }
        string assText = string.Join("", assSegments);
        string startFormatted = MillisecondsToTime(start);
        string endFormatted = MillisecondsToTime(end);
        output.Add($"Dialogue: 0,{startFormatted},{endFormatted},Default,,0,0,0,,{assText}");
    }

    File.WriteAllLines(assPath, output);
    Console.WriteLine("QRC -> ASS 转换完成！");
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