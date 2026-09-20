using MfgEnabler;

void Check(string name, string input, string expected)
{
    string actual = Updates.ExtractEnglishNotes(input).Replace("\r\n", "\n");
    if (actual != expected) throw new Exception($"{name}: {actual}");
    Console.WriteLine("PASS " + name);
}

Check("Mixed language release", "## English\r\nFixes a crash.\r\n\r\n## 中文\r\n修复崩溃。\r\n한국어 설명\r\n日本語の説明", "Fixes a crash.");
Check("Markdown cleanup", "# EN\n**Fix** `version.dll`: [details](https://example.com/issue)", "Fix version.dll: details");
Check("Paragraphs", "\nFirst fix.\n\n\nSecond fix.\n", "First fix.\n\nSecond fix.");
Check("English only", "On 0.3.x, just replace version.dll.", "On 0.3.x, just replace version.dll.");
Check("Empty body", null, "No release notes were provided.");
Check("No English", "修复问题\n한국어 설명", "See the upstream release page for details.");
