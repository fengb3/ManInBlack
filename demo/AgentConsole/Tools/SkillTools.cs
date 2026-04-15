using ManInBlack.AI.Attributes;

namespace AgentConsole.Tools;

[ServiceRegister.Singleton]
public partial class SkillTools
{
    private static Dictionary<string, SkillEntry> _skills = new();
    
    public SkillTools()
    {
        // TODO: Get skill dir from some sort of 'skill base dir provider' service, allowing dynamic updates without restarting the app
        InitializeSkills(Path.Combine(Directory.GetCurrentDirectory(), "skills"));
    }

    /// <summary>
    /// Load specialized knowledge by name.
    /// </summary>
    /// <param name="skillName">skill name to load</param>
    /// <returns>skill content if success</returns>
    [AiTool]
    public string LoadSkill(string skillName) => GetContent(skillName);

    /// <summary>
    /// get skill from file to ram
    /// </summary>
    /// <param name="skillsDir"></param>
    private static void InitializeSkills(string skillsDir)
    {
        if (!Directory.Exists(skillsDir))
        {
            Console.WriteLine(skillsDir);
            return;
        }

        foreach (
            var file in Directory
                .EnumerateFiles(skillsDir, "SKILL.md", SearchOption.AllDirectories)
                .Order()
        )
        {
            var text = File.ReadAllText(file);
            var (meta, body) = ParseFrontMatter(text);
            var name = meta.GetValueOrDefault(
                "name",
                Path.GetFileName(Path.GetDirectoryName(file)!) ?? "unknown"
            );
            _skills[name] = new SkillEntry(meta, body, file);
        }
    }

    /// <summary>Parse YAML front-matter between --- delimiters.</summary>
    private static (Dictionary<string, string> Meta, string Body) ParseFrontMatter(string text)
    {
        var match = SkillMetaRegex().Match(text);

        if (!match.Success)
            return ([], text);

        var meta = ParseYamlBlock(match.Groups[1].Value);
        return (meta, match.Groups[2].Value.Trim());
    }

    /// <summary>
    /// Minimal YAML parser that handles:
    ///   key: simple value
    ///   key: |          (literal block scalar — newlines preserved)
    ///   key: >          (folded block scalar  — newlines become spaces)
    ///   (indented continuation lines)
    /// </summary>
    private static Dictionary<string, string> ParseYamlBlock(string yaml)
    {
        var result = new Dictionary<string, string>();
        var lines  = yaml.Split(['\r', '\n']);

        string? currentKey  = null;
        bool    isLiteral   = false;// |
        bool    isFolded    = false;// >
        int     blockIndent = -1;
        var     blockLines  = new List<string>();

        void FlushBlock()
        {
            if (currentKey == null)
                return;
            if (isLiteral || isFolded)
            {
                var joined = isFolded
                    ? string.Join(' ', blockLines).Trim()
                    : string.Join('\n', blockLines).TrimEnd();
                result[currentKey] = joined;
            }
            blockLines.Clear();
            isLiteral   = isFolded = false;
            blockIndent = -1;
        }

        foreach (var raw in lines)
        {
            // inside a block scalar — continuation lines start with whitespace
            if ((isLiteral || isFolded) && raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                // detect indent from first content line
                if (blockIndent < 0)
                    blockIndent = raw.Length - raw.TrimStart().Length;

                blockLines.Add(raw.Length >= blockIndent ? raw[blockIndent..] : raw.TrimStart());
                continue;
            }

            // not indented → block ended (or we're in a block and hit an empty line)
            if (isLiteral || isFolded)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    blockLines.Add("");// preserve blank lines in literal blocks
                    continue;
                }
                FlushBlock();// next key starts
            }

            if (!raw.Contains(':'))
                continue;

            var parts = raw.Split(':', 2);
            var key   = parts[0].Trim();
            var val   = parts[1].Trim();

            if (val == "|")
            {
                FlushBlock();
                currentKey = key;
                isLiteral  = true;
            }
            else if (val == ">")
            {
                FlushBlock();
                currentKey = key;
                isFolded   = true;
            }
            else
            {
                FlushBlock();
                currentKey  = key;
                result[key] = val;
            }
        }

        FlushBlock();
        return result;
    }

    /// <summary>Layer 1: short descriptions for the system prompt.</summary>
    public string GetDescriptions()
    {
        if (_skills.Count == 0)
            return "(no skills available)";

        return string.Join(
            '\n',
            _skills.Select(kv => {
                var desc = kv.Value.Meta.GetValueOrDefault("description", "No description");
                var tags = kv.Value.Meta.GetValueOrDefault("tags", "");
                var line = $"  - {kv.Key}: {desc}";
                if (!string.IsNullOrEmpty(tags))
                    line += $" [{tags}]";
                return line;
            })
        );
    }

    /// <summary>Layer 2: full skill body returned in tool_result.</summary>
    public string GetContent(string name) =>
        !_skills.TryGetValue(name, out var skill)
            ? $"Error: Unknown skill '{name}'. Available: {string.Join(", ", _skills.Keys)}"
            : $"""
               <skill name="{name}" path="{skill.Path}">
               {skill.Body}
               </skill>
               """;

    private record SkillEntry(Dictionary<string, string> Meta, string Body, string Path);

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^---\r?\n(.*?)\r?\n---\r?\n(.*)",
        System.Text.RegularExpressions.RegexOptions.Singleline
    )]
    private static partial System.Text.RegularExpressions.Regex SkillMetaRegex();

}