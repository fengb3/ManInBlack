using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.ToolCallFilters;

namespace ManInBlack.AI.Tools;

[ServiceRegister.Scoped]
public partial class SkillTools(SkillService skillService)
{
    /// <summary>
    /// Load specialized knowledge by name.
    /// </summary>
    /// <param name="skillName">skill name to load</param>
    /// <returns>skill content if success</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string LoadSkill(string skillName) => skillService.GetContent(skillName);


    /// <summary>
    /// Install a skill from a file path. that file should have extension .skill
    /// </summary>
    /// <param name="skillFilePath">.skill file path</param>
    /// <returns>result of skill installation</returns>
    [AiTool]
    [AiTool.HasFilter<LoggingFilter, BroadCastingFilter>]
    public string InstallSkill(string skillFilePath) => skillService.InstallSkill(skillFilePath);
}