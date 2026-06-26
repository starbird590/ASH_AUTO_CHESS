using UnityEngine;

[CreateAssetMenu(fileName = "Skill_", menuName = "ASH Auto Chess/Skill Data")]
public class SkillDataSO : ScriptableObject
{
    public string SkillID;
    public int TargetType = 101;
    public string TriggerType = "1";
    public int EffectType = 101;
    public string EffectParams;
}
