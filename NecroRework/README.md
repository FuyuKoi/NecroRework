# 亡灵契约师·重制 —— 工程说明

一个《杀戮尖塔2》mod 的完整源代码工程。当前版本 **v0.6**。

---

## 一、这个项目是什么

通过 Harmony 运行时补丁，修改原版「亡灵契约师」的卡牌行为，并新增卡牌 / 附魔。

**不修改游戏本体任何文件**：所有改动在游戏启动时注入，删掉 mod 文件夹即恢复原版。

---

## 二、目录结构

```
NecroRework/
├─ NecroRework.csproj        工程文件（含游戏路径 Sts2Dir）
├─ NecroRework.json          mod 清单（名字、版本、依赖）
├─ src/
│  ├─ Main.cs                入口：挂补丁、注册新卡
│  ├─ PatchInstaller.cs      表驱动补丁：按 Reworks 表动态挂 OnPlay / CanonicalVars
│  ├─ Reworks.cs             ★ 原版卡改动都写在这里（数值 / 效果两张表）
│  ├─ LocOverrides.cs        把 localization 下的 JSON 写进游戏文本表
│  ├─ OstySplit.cs           「为你而死」50% 分摊（含防重复结算）
│  ├─ DriftPatches.cs        飘荡附魔：费用+1 / 虚无 / 消耗给4格挡
│  ├─ DoomUtil.cs            灾厄工具：自伤 / 转移给敌人 / 转移给奥斯提 / 纯移除
│  ├─ Enchantments/
│  │  └─ DriftEnchantment.cs 飘荡附魔本体（可附任何卡，含状态与诅咒）
│  └─ Cards/
│     └─ DriftTestCard.cs    调试卡（正式卡表定下来后删除）
└─ localization/zhs/
   ├─ cards.json             卡牌文本（key = 卡牌ID.title / .description）
   ├─ enchantments.json      附魔文本
   └─ powers.json            能力文本（含「为你而死」说明）
```

`lib/BaseLib.dll` 仅用于反编译参考，**不参与编译**，压缩包中未包含。

---

## 三、怎么编译

前置：安装 .NET SDK 9 或更高（<https://dotnet.microsoft.com/download>）。

```powershell
# 1. 如果游戏不在 F 盘，改这一行：
#    NecroRework.csproj 里的 <Sts2Dir>F:\SteamLibrary\steamapps\common\Slay the Spire 2</Sts2Dir>

# 2. 编译
dotnet build -c Release

# 3. 产物
#    bin\Release\NecroRework.dll
#    bin\Release\NecroRework.json
#    bin\Release\localization\
```

把这三样放进：

```
Slay the Spire 2\mods\NecroRework\
```

重启游戏即可。

---

## 四、怎么加一张卡 / 改一张卡

### 改原版卡的数值

在 `src/Reworks.cs` 的 `Vars` 表里加一行：

```csharp
[typeof(Bodyguard)] = _ => [new SummonVar(8m)],
```

描述里的 `{Summon:diff()}` 会自动跟着变，**不用改文本**。

### 改原版卡的效果

在 `src/Reworks.cs` 的 `Play` 表里加一条（`card` / `ctx` / `play`）：

```csharp
[typeof(BlightStrike)] = async (card, ctx, play) =>
{
    await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
        .FromCard(card, play)
        .Targeting(play.Target)
        .Execute(ctx);

    await DoomUtil.ApplyToSelf(ctx, card, 3m);   // 例：给自己 3 层灾厄
},
```

### 加一张新卡

1. 在 `src/Cards/` 新建一个类，继承 `CardModel`
2. 在 `src/Main.cs` 的 `RegisterContent()` 里注册进卡池：
   ```csharp
   ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(你的卡));
   ```
3. 在 `localization/zhs/cards.json` 加文本，key 是 `类名转大写蛇形.title`
   （`SoulTax` → `SOUL_TAX.title`）

---

## 五、灾厄相关的标准写法

`DoomUtil.cs` 里已经封装好，**不要自己直接调 `PowerCmd.Apply<DoomPower>`**，否则判定次数会和其他卡不一致：

| 方法 | 效果 |
|---|---|
| `ApplyToSelf(ctx, card, n)` | 给自己上灾厄：触发死亡之门 / 厄运之衣，**不触发**血肉戏法 |
| `ApplyToOsty(ctx, card, n)` | 给奥斯提上灾厄：同上 |
| `TransferToEnemy(ctx, card, enemy, max)` | 先扣角色、再扣奥斯提，最后对敌人**只判定一次** |
| `TransferToOsty(ctx, card, max)` | 角色 → 奥斯提，只判定一次 |
| `RemoveDoom(ctx, card, target, n)` | 纯移除：不产生任何判定 |

---

## 六、已知限制

- 游戏版本 **0.113.1**；游戏更新后方法签名可能变化，需重新核对
- 「飘荡」附魔目前用游戏占位图标
- `DriftTestCard` 是临时调试卡，正式卡表完成后删除
- X 费卡不 +1 费，但仍有虚无与消耗起防

---

## 七、备份

工作区根目录有一套自动快照系统：

- 每次打包自动生成 `backups\<时间戳>\`（完整源码 + 成品 zip）
- 保留最近 12 份
- `恢复.bat` 可按序号回退

详见 `备份说明.txt`。

---

## 八、当前已改动的卡（v0.6）

| 卡 | 改动 |
|---|---|
| 护卫 Bodyguard | 召唤 5(7) → **8(10)** |
| 出击 Unleash | 1 费，奥斯提 6(9) 伤害；本场每获得过 2 点召唤额外 +1 伤害 |
| 剥夺 Reave | 伤害 10(13) → **11(14)**；灵魂部分不变 |
| 玷污 Defile | 伤害 13(17) → **12(16)**；新增「转移至多 6(8) 层灾厄给该敌人」 |
| 荒疫打击 BlightStrike | 伤害 8(10) → **10(12)**；新增「先获得 5 点灾厄」 |
| 响指 Snap | 奥斯提 7(10) 伤害不变；保留张数 → **1(2)** |
| 鬼火 Wisp | 获得 1 费 → **2 费**；新增「获得 6 点灾厄」 |
| 鞭打 Scourge | 给予 13(16) → **14(17)**；新增「转移至多 7 层灾厄」；抽牌固定 **2** 张 |
| 负能量脉冲 NegativePulse | 格挡 5(6) → **7(8)**，全体灾厄 7(11) → **9(13)**；新增「获得 6 点灾厄」 |
| 唤起 Invoke | 下回合召唤 2(3) → **4(6)**；费用部分不变 |
| 来生 Afterlife | 召唤 6(9) → **10(13)**；免疫灾厄改为「奥斯提每 14 点生命值挡一次」（当前生命值、向下取整） |
| 守墓人 GraveWarden | 格挡不变；灵魂随卡升级变成 **灵魂+** |
| 违逆 Defy | 格挡 6(9) → **7(10)**，虚弱 1 → **1(2)**；新增「获得 8 点灾厄」 |
| 吸引仇恨 PullAggro | 召唤 4(5) → **8(10)**；格挡换成「奥斯提为你承受所有伤害 1(2) 回合」；附赠 debuff「已死的维系者」：期间奥斯提每失 2 血你 +1 灾厄 |
| 回眸 LookBack（**新增卡**） | 1 费普通技能：获得 7(10) 格挡，把消耗牌堆里的一张灵魂，或一张没有「消耗」的虚无牌放到抽牌堆顶 |

另外，「为你而死」改为奥斯提只吸收 50% 未格挡伤害（向上取整）；
「吸引仇恨」期间临时回到 100% 吸收（不下放一半给玩家）。

### v0.4.8 修复

- 50% 分摊改为：**玩家那一半交回引擎原本的扣血流程**（格挡、易伤、伤害数字、孤注一掷全部原版行为），
  只把奥斯提那一半在结算后单独补算，且补算时不再经过力量／易伤修正。
  修掉了两处旧问题：有易伤时伤害被乘两遍（实际掉血远超显示）、玩家身上会播两次受击动画。
- 日志里每次分摊都会打印 `未格挡 X → 奥斯提 Y（HP …）／玩家 Z（HP …）`，便于核对。
- 新增两个自制能力：**灾厄免疫**（DoomWardPower，挡下一次灾厄后消失）与
  **仇恨守护**（OstyGuardPower，挂在奥斯提身上，敌方回合结束 −1 层）。
  图标分别借用原版「人工制品」与「为你而死」的图，避免显示空白。

### v0.4.9 改动

- **来生 Afterlife**：免疫灾厄从固定 1 次改为**按奥斯提当前生命值折算**——每 14 点生命值免疫一次，
  向下取整（27 → 1 次，28 → 2 次，10 → 0 次）。层数在**施放时一次性快照**，之后奥斯提掉血不会收回已到手的免疫。
  结算顺序改为「先召唤，再按召唤后的血量算层数」，与卡面文字顺序一致。
- **为你而死图标**：换成自制的「灰绿棺材 + 红玫瑰」图标（原图按 #99FF99、15% 不透明度涂抹），
  常驻图标与悬停大图都换，素材是工程里的 `die_for_you_icon.png`。
- **奥斯提血条**：透明度改为 75%（只动血条本体与血量数字，不影响出场／退场补间）。

### v0.4.10 新增

- **攻击意图伤害提示**：每个怪物的攻击意图数字后面追加一段暗红色（#C91515、90% 不透明度）的「（x）」，
  x 表示**你的格挡挡完这一手、再经过奥斯提减伤之后，玩家实际会掉的血量**：
  多段攻击按全部命中累计；奥斯提「吸引仇恨」期间按全额吸收计算；x 为 0 时不显示。
  数字会随格挡、奥斯提血量、力量／易伤实时刷新。
### v0.4.10 追补

- 「吸引仇恨」的增益改名叫 **吸引仇恨**（原来叫「仇恨守护」）。
- 新增 debuff **已死的维系者**（SpilloverPower，挂在玩家身上）：在「吸引仇恨」生效期间，
  奥斯提每失去 2 点生命值，玩家获得 1 层灾厄（以玩家为施加者，会触发死亡之门／厄运之衣，不触发血肉戏法）。
  层数与「吸引仇恨」同寿，敌方回合结束时一起减 1。

### v0.5 新增

- debuff「殃及」改名 **已死的维系者**（只改中文名，内部 id 仍是 SPILLOVER_POWER）。
- 新增卡牌 **回眸 LookBack**：1 费、普通品质、技能牌，获得 7(10) 点格挡，
  然后把「消耗牌堆」里的一张灵魂，或一张**没有「消耗」关键词的虚无牌**放到抽牌堆顶。
  已进亡灵契约师卡池，会像普通白卡一样在卡牌奖励等场合出现。
  卡图是工程里的 `look_back.png`（由 `CardArt` 这个 Harmony 前缀直接替换 `CardModel.Portrait`）。

### v0.6 改动

- **骨笛**：重做为「每回合**奥斯提第一次**攻击时，获得 5 点格挡」（原版是每次攻击 2 点）。
  `BoneFluteRework` 用 `AfterAttack` 前缀 + 每回合重置的 `HashSet<BoneFlute>` 控制次数，文案由 `localization/zhs/relics.json` 覆盖。
- **亡魂牵引 PullFromBelow**：重做为「你**被动消耗**下一张虚无牌时，改为自动打出，之后照常进入弃牌堆」，
  并获得同名 buff 计次；本场战斗中每打出过一张虚无牌，这张牌就造成 5(7) 点伤害一次。
  `PullFromBelowRedirect` 拦 `CardCmd.Exhaust`：手动打出带「消耗」的虚无牌属于主动消耗，不会被拦；
  洁净／散射炮／燃烧契约这类「被别人消耗」的效果仍会被改成自动打出。

