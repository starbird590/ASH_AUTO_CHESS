using UnityEngine;

[CreateAssetMenu(fileName = "UnitLogicData", menuName = "ASH Auto Chess/Unit Logic Data")]
public class UnitLogicDataSO : ScriptableObject
{
    public string chessId;
    public string chessName;
    public GameObject unitPrefab;
    public string unionId;
    public int faction;
    public int playerDirective;
    public int unitCost;
    public int unitPrice;
    public int unitRare;
    public int unitTier;
    public int unitType;
    public int attackType;
    public int baseHp;
    public int baseArmor;
    public int bayonetArmor;
    public float critRate;
    public float critDamage;
    public int fireDamage;
    public float fireRate;
    public float fireSpeed;
    public int fireRange;
    public int ammo;
    public int ammoSpeed;
    public float firePenPct;
    public float firePenFlat;
    public int damageAoe;
    public string bayonetId;
    public int bayonetDamage;
    public string bayonetCost;
    public float bayonetSpeed;
    public int bayonetRange;
    public float bayonetPenPct;
    public float bayonetPenFlat;
    public float moveSpeed;
    public float captureSpeed;
    public int threatValue;
}
