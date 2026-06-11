using UnityEngine;

/// <summary>
/// 全阵营通用子弹投射物。
/// 子弹只追踪并伤害 Launch 时灌入的那个特定目标，防止飞行途中误伤其他碰撞体。
/// </summary>
public class BulletProjectile : MonoBehaviour
{
    [SerializeField, Tooltip("子弹最大寿命，超过后自动销毁，避免内存泄漏。")]
    private float maxLifeTime = 5f;

    private UnitLogic attacker;
    private UnitLogic target;
    private float speed;
    private float lifeTimer;
    private bool launched;

    public void Launch(UnitLogic attackerUnit, UnitLogic targetUnit, float projectileSpeed)
    {
        attacker = attackerUnit;
        target = targetUnit;
        speed = Mathf.Max(0.01f, projectileSpeed);
        lifeTimer = 0f;
        launched = true;
    }

    private void Update()
    {
        if (!launched)
        {
            return;
        }

        lifeTimer += Time.deltaTime;
        if (lifeTimer >= maxLifeTime)
        {
            Destroy(gameObject);
            return;
        }

        if (target == null || !target.IsAlive || !target.gameObject.activeInHierarchy)
        {
            Destroy(gameObject);
            return;
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            target.transform.position,
            speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryApplyHit(other.GetComponentInParent<UnitLogic>());
    }

    private void OnTriggerEnter(Collider other)
    {
        TryApplyHit(other.GetComponentInParent<UnitLogic>());
    }

    private void TryApplyHit(UnitLogic hitUnit)
    {
        if (!launched || hitUnit == null || hitUnit != target)
        {
            return;
        }

        if (target != null && target.IsAlive && target.gameObject.activeInHierarchy)
        {
            target.ReceiveDamage(attacker, DamageType.Shooting);
        }

        Destroy(gameObject);
    }
}
