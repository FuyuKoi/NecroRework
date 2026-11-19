# 亡灵契约师·重制 —— 工程说明

一个《杀戮尖塔2》mod 的完整源代码工程。当前版本 **v0.4.4**。

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
