在对EnemyBase进行测试及code review的过程中，发现了诸多问题。本版本专门解决EnemyBase相关的问题

# 问题一

已知目前的巡逻点距离地面有一定距离。现在EnemyBase在靠近巡逻点时，会原地跳一下。请解决

# 问题二

我希望EnemyBase在离开Chase状态后，会先进入Idle状态一段时间，再往最近的巡逻点走，而不是直接就往巡逻点走

# 问题三

你在OnVisionEnter里有这样的写法：
if (StateName == "Stunned" || StateName == "Dead" || StateName == "Chase") return;

我感觉这种遍历所有StateName的写法很丑陋。你能换个写法吗？比如

if (状态是不可移动状态 || StateName == "Chase") return;

# 问题四

我发现个现象：我没在Inspector上设置mInteractionZones的时候，操作Player与子物体上带有Back的ZoneTag交互，也会被刺成功。为啥？