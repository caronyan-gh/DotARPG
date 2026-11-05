using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotARPG.Core;

// Translate only at the display boundary. Saved names, logs and simulation stay language independent.
public static class GameText
{
    public static bool English { get; set; }
    private static readonly Dictionary<string, string> words = Read(Words + "\n" + Phrases);
    private static readonly (Regex Pattern, string Format)[] templates = Read(Templates)
        .Select(pair => (Compile(pair.Key), pair.Value)).ToArray();
    private static readonly Dictionary<string, string> cache = new();

    public static string Text(string value)
    {
        if (!English || string.IsNullOrEmpty(value)) return value;
        if (cache.TryGetValue(value, out string? cached)) return cached;
        string translated = Translate(value);
        if (cache.Count > 8192) cache.Clear();
        cache[value] = translated;
        return translated;
    }

    private static string Translate(string value)
    {
        if (words.TryGetValue(value, out string? exact)) return exact;
        foreach (var template in templates)
        {
            Match match = template.Pattern.Match(value);
            if (!match.Success) continue;
            object[] arguments = Enumerable.Range(0, match.Groups.Count - 1)
                .Select(i => (object)Text(match.Groups["p" + i].Value)).ToArray();
            return string.Format(CultureInfo.InvariantCulture, template.Format, arguments);
        }
        if (value.Contains('\n')) return string.Join('\n', value.Split('\n').Select(Text));
        if (value.EndsWith("の守護者")) return "Guardian of " + Text(value[..^4]);
        if (value.EndsWith("の支配域")) return "Domain of " + Text(value[..^4]);
        Match sequel = Regex.Match(value, @"^(.+) ([IVXLCDM]+)$");
        if (sequel.Success) return Text(sequel.Groups[1].Value) + " " + sequel.Groups[2].Value;
        Match level = Regex.Match(value, @"^(.+?)(\s+Lv\.\d+)$");
        if (level.Success) return Text(level.Groups[1].Value) + level.Groups[2].Value;
        if (value.Contains('・'))
        {
            string[] parts = value.Split('・', 2);
            return Text(parts[1]) + " the " + Text(parts[0]);
        }
        foreach (var (suffix, format) in TitleEndings)
            if (value.EndsWith(suffix)) return string.Format(CultureInfo.InvariantCulture, format, Text(value[..^suffix.Length]));
        foreach (var (prefix, english) in PlacePrefixes)
            if (value.StartsWith(prefix) && words.TryGetValue(value[prefix.Length..], out string? place)) return english + place;
        if (value.EndsWith('。')) return Text(value[..^1]) + ".";
        return value;
    }

    private static readonly (string, string)[] PlacePrefixes =
        [("遠つ", "Distant "), ("奥", "Inner "), ("北の", "Northern "), ("旧", "Old "),
         ("上", "Upper "), ("古の", "Ancient "), ("古", "Ancient "), ("東の", "Eastern ")];
    private static readonly (string, string)[] TitleEndings =
        [("秘宝譚", "Treasures of {0}"), ("帰郷譚", "Homeward through {0}"), ("封魔録", "Seals of {0}"),
         ("の聖遺物", "Relics of {0}"), ("の旅人", "Wayfarer of {0}"), ("の誓約", "Oath of {0}"),
         ("剣譜", "Sword Chronicles of {0}"), ("侠客伝", "Heroes of {0}"), ("封妖記", "Demon Seals of {0}"),
         ("の星詠み", "Stargazer of {0}"), ("の隊商", "Caravan of {0}"), ("の魔神譚", "Djinn of {0}")];

    private static Dictionary<string, string> Read(string source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in source.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = line.IndexOf('|');
            result.Add(line[..separator], line[(separator + 1)..]);
        }
        return result;
    }

    private static Regex Compile(string source)
    {
        var pattern = new StringBuilder("^");
        int start = 0;
        foreach (Match slot in Regex.Matches(source, @"\{(\d+)\}"))
        {
            pattern.Append(Regex.Escape(source[start..slot.Index]));
            pattern.Append("(?<p").Append(slot.Groups[1].Value).Append(">.+?)");
            start = slot.Index + slot.Length;
        }
        pattern.Append(Regex.Escape(source[start..])).Append('$');
        return new Regex(pattern.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    private const string Words = """
和風|Japanese
洋風|Western
中華風|Chinese
砂漠・オリエンタル|Desert / Oriental
葦原|Ashihara
朧月|Hazy Moon
八雲|Yakumo
常世|Tokoyo
エルドリア|Eldoria
アッシュベイル|Ashvale
リンドヴァル|Lindval
ミストリア|Mistria
青嵐|Qinglan
天霞|Tianxia
雲夢|Yunmeng
玄天|Xuantian
ザフラーン|Zafran
アルマジール|Almajir
サハリヤ|Sahariya
ヌール|Nur
旅の浪人|Ronin
若い巫女|Shrine Maiden
名もなき忍び|Nameless Shinobi
山伏|Yamabushi
駆け出しの騎士|Novice Knight
森の狩人|Forest Hunter
放浪の魔術師|Wandering Mage
旅の吟遊詩人|Bard
流浪の剣客|Wandering Swordsman
若い道士|Young Taoist
山里の拳士|Mountain Boxer
薬売りの娘|Herbalist
隊商の護衛|Caravan Guard
星読みの娘|Stargazer
砂漠の盗賊|Desert Rogue
放浪の錬金術師|Wandering Alchemist
朝霧の里|Morningmist Village
白狐の社|White Fox Shrine
彼岸の森|Higan Forest
月見峠|Moonwatch Pass
水鏡の宿|Mirrorwater Inn
鬼哭城|Demoncry Castle
桜隠れ|Hidden Blossom
黄泉の祠|Yomi Shrine
灰の砦|Ash Fortress
白樺の村|Birch Village
古王の墓|Old King's Tomb
霧の森|Mistwood
銀の修道院|Silver Abbey
竜の尖塔|Dragon Spire
小麦の宿|Wheatsheaf Inn
星降る遺跡|Starfall Ruins
桃源郷|Peach Blossom Haven
青龍廟|Azure Dragon Temple
竹海|Bamboo Sea
白雲峰|White Cloud Peak
龍門の宿|Dragon Gate Inn
黒蓮宮|Black Lotus Palace
玉泉の里|Jade Spring Village
仙人の洞|Immortal's Cave
月のオアシス|Moon Oasis
瑠璃の市場|Lapis Bazaar
蜃気楼の宮殿|Mirage Palace
風の砂丘|Wind Dunes
琥珀の隊商宿|Amber Caravanserai
忘却の都|Forgotten City
香木の庭|Sandalwood Garden
星の霊廟|Tomb of Stars
野犬|Wild Dog
化け狸|Tanuki
落武者|Fallen Samurai
餓鬼|Hungry Ghost
山の鬼|Mountain Oni
灰狼|Ash Wolf
ゴブリン|Goblin
骸骨兵|Skeleton Soldier
夜の盗賊|Night Bandit
ワイバーン|Wyvern
山賊|Bandit
キョンシー|Jiangshi
蛇妖|Serpent Demon
黒風の妖兵|Blackwind Soldier
虎の精|Tiger Spirit
砂サソリ|Sand Scorpion
砂賊|Sand Bandit
砂の亡霊|Desert Wraith
炎の精|Fire Spirit
大砂蛇|Great Sand Serpent
鏡の欠片|Mirror Shard
鎮めの鈴|Calming Bell
月読の勾玉|Tsukuyomi's Jewel
王の紋章|Royal Crest
星の結晶|Star Crystal
黎明の聖杯|Chalice of Dawn
龍玉の欠片|Dragon Orb Shard
仙桃|Immortal Peach
天書の巻物|Celestial Scroll
星図の断片|Star Chart Fragment
魔神の封印|Djinn Seal
暁の宝珠|Dawn Orb
百目の鬼|Hundred-Eyed Oni
黒衣の陰陽師|Black-Robed Onmyoji
九尾の化生|Nine-Tailed Spirit
灰の竜|Ash Dragon
不死の王|Undying King
黄昏の魔女|Twilight Witch
黒蓮の妖帝|Black Lotus Emperor
千年の蛇仙|Ancient Serpent Sage
堕ちた龍王|Fallen Dragon King
砂塵の魔神|Djinn of the Sands
硝子の暴君|Glass Tyrant
七つ目のイフリート|Seven-Eyed Ifrit
蓮|Ren
鈴|Suzu
楓|Kaede
朔|Saku
千景|Chikage
蛍|Hotaru
リオ|Rio
エリン|Erin
アッシュ|Ash
セラ|Sera
ノア|Noah
ルカ|Luca
青蘭|Qinglan
白玲|Bailing
天佑|Tianyou
飛燕|Feiyan
子龍|Zilong
月華|Yuehua
ラシード|Rashid
ライラ|Layla
サミル|Samir
ナディア|Nadia
ファリド|Farid
ヌーラ|Nura
こはく|Kohaku
なぎ|Nagi
こまち|Komachi
レイ|Rei
ゆず|Yuzu
シオン|Shion
つむぎ|Tsumugi
ハル|Haru
すい|Sui
トト|Toto
ななし|Nameless
常夜|Evernight
白狐|White Fox
花霞|Blossom Haze
灰冠|Ashcrown
暁光|Dawnlight
霧影|Mistshadow
星霜|Starlit Years
銀月|Silver Moon
白雲|White Cloud
龍泉|Dragon Spring
砂海|Sandsea
琥珀|Amber
千夜|Thousand Nights
瑠璃|Lapis
流星|Falling Star
錆びた脇差|Rusty Wakizashi
鉄打ちの太刀|Iron Tachi
銀打ちの太刀|Silver Tachi
鬼断ちの神刀|Oni-Slayer Blade
旅人の短剣|Traveler's Dagger
鋼の長剣|Steel Longsword
ミスリルソード|Mithril Sword
竜断ちの聖剣|Dragonbane Sword
古びた柳葉刀|Worn Liuyedao
青鋼の剣|Blue Steel Sword
玄鉄の剣|Dark Iron Sword
龍淵・斬妖剣|Longyuan Demonbane
欠けた曲刀|Chipped Scimitar
鋼の曲刀|Steel Scimitar
月銀の曲刀|Moonsilver Scimitar
暁の王剣|Dawn King's Blade
旅装|Travel Clothes
革のアーマー|Leather Armor
鋼のアーマー|Steel Armor
護国の鎧|Guardian Armor
竜鱗の鎧|Dragonscale Armor
玄武の仙甲|Black Tortoise Armor
魔神避けの胴衣|Djinnward Vest
なし|None
革のヘルム|Leather Helm
鋼のヘルム|Steel Helm
星守のヘルム|Starward Helm
革のグローブ|Leather Gloves
鋼のグローブ|Steel Gloves
雷紋のグローブ|Thunder Gloves
旅人の靴|Travel Boots
革のブーツ|Leather Boots
鋼のブーツ|Steel Boots
風走りのブーツ|Windrunner Boots
木守りの護符|Wooden Charm
銀の護符|Silver Charm
封魔の護符|Demonseal Charm
傷薬|Healing Potion
霊薬|Mana Potion
白銀の太刀|White Silver Tachi
山伏の護符|Yamabushi Charm
緋色の羽織|Scarlet Haori
ルーンの指輪|Rune Ring
銀糸の外套|Silverthread Cloak
八卦の護符|Bagua Charm
仙鶴の道衣|Crane Robe
月銀のシャムシール|Moonsilver Shamshir
瑠璃の護符|Lapis Charm
星織りの衣|Starwoven Robe
里外れの平原|Village Plains
妖しの森|Haunted Woods
鬼の山路|Oni Mountain Trail
黄泉の奥地|Depths of Yomi
はじまりの平原|First Plains
辺境の森|Frontier Woods
竜の山地|Dragon Highlands
灰の奥地|Ashen Depths
郷里の平原|Homeland Plains
霊竹の山野|Spirit Bamboo Hills
仙山の峠|Immortal Mountain Pass
黒雲の奥地|Blackcloud Depths
オアシスの草原|Oasis Grasslands
隊商の砂丘|Caravan Dunes
赤岩の峡谷|Redrock Canyon
忘却の砂海|Forgotten Sandsea
""";

    private const string Phrases = """
プレイデータ|Player Data
装 備|EQUIPMENT
武器|Weapon
防具|Armor
ヘルム|Helm
グローブ|Gloves
ブーツ|Boots
護符|Charm
所 持 品|INVENTORY
地図の点|MAP LEGEND
旅 の 記 録|JOURNAL
旅人|Traveler
集落|Settlement
獣|Beast
人型|Humanoid
不死者|Undead
妖魔|Spirit
強敵|Elite
中ボス|Guardian
ボス|Boss
冒険を眺める|Watch Adventure
次の冒険を開始|Next Adventure
拠点から再挑戦|Revive at Base
このゲームを別のプレイヤーで|Replay with Another Player
終了|Exit
次の世界 ↗|Next World ↗
明るい地形：踏破済　／　暗い斜線：未踏|Bright terrain: explored / Dark hatching: unexplored
SPACE  一時停止     N  次の世界     F11  全画面|SPACE Pause     N Next world     F11 Full screen
時を止めています|Paused
次の世界の雰囲気を選ぶ|Choose the next world's setting
一時停止 / 再開（Space）|Pause / Resume (Space)
眺める速さ|Playback speed
新しい世界と物語（N）|New world and story (N)
おまかせ|Random
おまかせ（毎回変わる）|Random (each world)
旅を終えた|Journey complete
倒れた|Fallen
離脱中|Retreating
回避中|Dodging
攻撃を警戒|Watching attacks
交戦中|In combat
宿へ撤退中|Returning to inn
敵に追われている|Being pursued
装備を調達|Buying gear
鍛錬中|Training
未踏地を探索|Exploring
探索中|Searching
移動中|Traveling
交戦中の敵：—|Enemy: —
回避！|Dodge!
攻撃予兆…|Attack incoming...
かわした！　反撃の好機|Dodged! Counterattack!
被弾！|Hit!
敵の隙へ反撃|Counterattacking
間合いを詰める|Closing in
旅の記憶をたどっています|Retracing the Journey
前回閉じた場所から再開します|Resuming where you left off
少しだけお待ちください。|Please wait a moment.
PROLOGUE  /  ふたたびの旅|PROLOGUE / A Journey Renewed
PROLOGUE  /  小さな旅のはじまり|PROLOGUE / A Small Beginning
EPILOGUE  /  旅の完結|EPILOGUE / Journey's End
EPILOGUE  /  ここで途絶えた旅|EPILOGUE / A Journey Cut Short
失われた宝を求めて。|In search of a lost treasure.
捕らわれた人々を救うために。|To rescue those held captive.
目覚めた災いを、もう一度封じるために。|To seal an awakened evil once more.
小さな点が、この広い世界を歩き始める。|A tiny dot takes its first steps into a vast world.
次の冒険を選ぶまで、この旅の終わりに留まります。|This journey will wait here until you choose the next adventure.
×や「終了」で閉じても、この画面から再開できます。|Close with × or Exit to resume from this screen later.
夜が明けなくなった|Dawn no longer comes
里から人影が消え始めた|People have begun vanishing from the villages
山の封印が破られた|The mountain's seal has been broken
黒い霧が国を覆った|A black mist has covered the kingdom
古い竜が目を覚ました|An ancient dragon has awakened
死者が街道を歩き始めた|The dead have begun walking the roads
龍脈が濁り始めた|The dragon veins have become corrupted
仙山が黒雲に包まれた|Black clouds have swallowed the sacred mountains
不老の秘薬が災いを招いた|An elixir of immortality has brought calamity
オアシスの水が枯れ始めた|The oases have begun to dry up
砂嵐の中に都が現れた|A city has appeared within the sandstorms
封じられた魔神が目を覚ました|A sealed djinn has awakened
山々に、朝の光が戻った。|Morning light returned to the mountains.
桜が咲く頃、その名は伝説になった。|By the time the cherry blossoms bloomed, that name had become legend.
旅人は刀を納め、どこかへ去った。|The traveler sheathed their blade and vanished into the distance.
谷に鐘の音が響き、霧は晴れた。|Bells rang through the valley, and the mist lifted.
小さな村で、新しい歌が生まれた。|In a little village, a new song was born.
王冠を断り、旅人はまた歩き出した。|Declining the crown, the traveler set out once more.
龍脈は澄み、桃の花が開いた。|The dragon veins ran clear, and peach blossoms opened.
仙山の雲間に、青い空が戻った。|Blue skies returned above the sacred mountains.
その剣客の行方を、誰も知らない。|No one knows where the swordsman went.
枯れた泉に、再び水が湧いた。|Water flowed once more from the dried spring.
隊商は星を頼りに、新しい道を進む。|Guided by the stars, the caravan found a new road.
砂はすべてを隠した。物語だけを残して。|The sands hid everything. Only the story remained.
宿代を払い、傷を癒やした。|Paid for a room and recovered.
宿代が足りない。集落の端で野営した。|Not enough gold for an inn. Camped outside the settlement.
宿代が足りない。集落で長めに野営し、HPとMPを全回復した。|Not enough gold for an inn. Camped in the settlement until HP and MP were fully restored.
店で傷薬を一つ補充した。|Bought a healing potion.
この先は手強そうだ。まず周辺の平原で腕を磨く。|The road ahead looks dangerous. Time to train on the nearby plains.
まだ訪れていない土地を探る|Search lands not yet visited
まだ足を運んでいない場所がある。少し寄り道をしよう。|There are places still unexplored. Time for a detour.
焚き火の跡。捕らわれた人々は、まだ生きている。|Remains of a campfire. The captives are still alive.
依頼の礼に、25 Gを受け取った。|Received 25 G as a reward.
牢の鍵を砕いた。人々は故郷へ逃げ出した。|The prison lock shattered. The captives fled for home.
傷薬を使い、体勢を立て直した。|Used a healing potion and regained footing.
霊薬で魔力を補った。|Restored mana with a potion.
異様に堅い守りだ。踏み込まず、まだ訪れていない土地から調べ直そう。|An unusually strong defense. Better withdraw and investigate unexplored lands.
間合いを切り、逃げ道を探す。|Breaking away and looking for an escape route.
手応えがない……何かがおかしい。一度引いて、まだ行っていない土地を探ろう。|The attacks do nothing... Something is wrong. Retreat and search unexplored lands.
間合いを取り、術で追撃した！|Kept a safe distance and struck with magic!
ここはまだ早かった……次は、まだ行っていない土地を探ろう。|Too soon to come here... Next time, search unexplored lands.
このままでは勝てそうにない。まだ行っていない土地に手がかりがあるかもしれない。|This fight cannot be won yet. Unexplored lands may hold a clue.
傷薬を拾った。|Found a healing potion.
まだ、この地に残る強敵がいる。旅は終わらない。|Powerful enemies still remain. The journey is not over.
最後の敵は倒した。だが、まだ歩いていない場所がある。|The final enemy has fallen, but there are still lands to explore.
道端で小銭を拾った。|Found a few coins by the road.
薬草を摘み、傷薬を一つこしらえた。|Gathered herbs and brewed a healing potion.
遠くで、寺の鐘が鳴った。|A temple bell rang in the distance.
道祖神に、そっと手を合わせた。|Offered a quiet prayer at a roadside shrine.
風に混じって、笛の音がした。|The sound of a flute drifted on the wind.
狐が一匹、道を横切った。|A fox crossed the road.
峠の茶屋で、噂を聞いた。|Heard a rumor at a mountain teahouse.
古い道標には、読めない文字。|An old signpost bore unreadable letters.
行き交う商人と、パンを分け合った。|Shared bread with a passing merchant.
廃墟の窓に、一瞬だけ灯りが見えた。|For a moment, a light flickered in a ruined window.
風が、森の奥から何かを運んでくる。|The wind carried something from deep within the woods.
誰かの足跡が、街道から逸れている。|Someone's footprints led away from the road.
竹林の奥から、琴の音が流れてきた。|The sound of a zither drifted from the bamboo grove.
茶売りの老人が、仙山の噂をした。|An old tea seller spoke of the sacred mountains.
白い鶴が、雲に消えた。|A white crane vanished into the clouds.
崖の石段に、古い詩が刻まれていた。|An ancient poem was carved into the cliffside steps.
霧の向こうに、楼閣が見えた。|A pavilion appeared beyond the mist.
隊商とすれ違った。香辛料の匂い。|A caravan passed, leaving the scent of spices.
砂の中から、古い銀貨を拾った。|Found an old silver coin in the sand.
星の並びを確かめ、歩き続ける。|Checked the stars and kept walking.
蜃気楼の街は、近づくと消えた。|The mirage city vanished as the traveler approached.
遠い祈りの声が、砂丘を渡った。|Distant prayers drifted across the dunes.
この小さな旅は、伝説になった|This little journey became a legend
ひとつの旅が、ここで終わった|A journey ended here
また別の世界で、新しい物語が待っている。|A new story awaits in another world.
前回の旅の続きに戻った。|Resumed the previous journey.
前回の旅を再現できませんでした。新しい旅を始めます。|Could not restore the previous journey. Starting a new one.
プレイヤーたちの記録|Player Records
作品|Adventure
状態|Status
死亡|Deaths
日数|Days
踏破|Explored
クリア|Cleared
中断|Interrupted
冒険中|Adventuring
不明な作品|Unknown Adventure
プレイデータを読み出せませんでした。|Could not load player records.
プレイデータを保存できませんでした。|Could not save player records.
保存できませんでした。|Could not save.
直前の保存を読めなかったため、ひとつ前の保存を確認しています。|The latest save could not be read. Checking the previous save.
前回の途中保存を読み出せませんでした。保存ファイルは残して、新しい旅を開きます。|Could not load the saved journey. The save is preserved; starting a new journey.
途中保存を書き込めませんでした。空き容量や保存先の状態を確認してください。|Could not write the save. Check free space and the save location.
途中保存を書き込めませんでした。このまま終了しますか？|Could not save the journey. Exit anyway?
前回の保存は残っています。|The previous save is preserved.
旅の記録を保存できませんでした。|Could not save the chronicle.
旅の記憶を読み出せませんでした。|Could not load the chronicle.
ひとつ前の旅の記憶を読み出しました。|Loaded the previous chronicle.
旅の記憶を保存できませんでした。|Could not save the chronicle.
言語設定を保存できませんでした。|Could not save the language preference.
""";

    private const string Templates = """
『{0}』|{0}
{0} ―― {1}|{0} — {1}
かつて{0}を旅した、{1}。|{1}, who once traveled through {0}.
前の旅では、{0}|Previously: {0}
{0}が{1}を発った。|{0} set out from {1}.
{0}は、{1}を発つ。|{0} sets out from {1}.
失われた{0}を探す旅が始まる。|The search for the lost {0} begins.
{0}に連れ去られた人々を探す。|Searching for those taken by {0}.
{0}を封じる術を求めて。|Seeking a way to seal {0}.
近くの{0}はLv.{1}。この先はまだ危険そうだ。未踏の土地から調べよう。|A nearby {0} is Lv.{1}. Too dangerous ahead. Better explore elsewhere first.
{0}へ入った。敵の気配が変わった。|Entered {0}. The enemies here feel different.
{0}で武器と防具を整える|Buy weapons and armor in {0}
手がかりを求め、{0}のもとへ|Seek clues from {0}
ボス対策の装備を求め、{0}へ|Seek better equipment from {0}
未踏の陸地を歩く · 踏破 {0}|Explore unvisited lands · {0} explored
Lv.{0}になり、{1}への旅支度を整えた。|Reached Lv.{0} and prepared to travel to {1}.
{0}で行方を追う|Track the missing people in {0}
{0}で手がかりを探す|Search for clues in {0}
{0}へ救出に向かう|Travel to {0} to rescue the captives
{0}へ{1}を探しに|Search for {1} in {0}
{0}で{1}に挑む|Challenge {1} in {0}
周辺で鍛錬 Lv.{0} → {1} / 次：{2}|Train nearby: Lv.{0} → {1} / Next: {2}
装備を整える資金集め · {0} G|Earn gold for equipment · {0} G
消耗が激しい。{0}へ戻る。|Worn out. Returning to {0}.
{0}で休息と補給|Rest and resupply in {0}
{0}にたどり着いた。|Arrived at {0}.
石碑に{0}の在り処が記されていた。|A stone tablet revealed the location of {0}.
古い書に{0}の真の名を見つけた。|An old book revealed the true name of {0}.
{0}を手に入れた。淡い光が宿る。|Obtained {0}. It glows faintly.
{0}に封印の力が満ちた。|{0} is filled with the power to seal evil.
{0}を保管した。装備にはLv.{1}が必要。|Stored {0}. Requires Lv.{1} to equip.
{0}を装備した。|Equipped {0}.
{0}が立ちはだかる。最後の戦い。|{0} stands in the way. The final battle begins.
中ボス・{0} Lv.{1}が行く手を阻む！|{0}, Lv.{1}, blocks the way!
{0} Lv.{1}が襲いかかってきた！|{0}, Lv.{1}, attacks!
{0} Lv.{1}と遭遇した。|Encountered {0}, Lv.{1}.
{0}の攻撃を、踏み込んでかわした！|Dodged {0}'s attack!
{0}の攻撃が命中。{1}の傷を負った。|{0}'s attack hit for {1} damage.
{0}から離脱した。鍛え直して出直そう。|Escaped from {0}. Time to train and try again.
{0}を守る力が弱まった気がする。|The power protecting {0} seems weaker.
{0}が、{1}を残した。|{0} dropped {1}.
Lv.{0}に上がった。少しずつ、力がついてきた。|Reached Lv.{0}. Growing stronger, little by little.
{0}を倒した。|Defeated {0}.
{0}は、帰らぬ人となった。|{0} never returned.
{0}で目を覚ました。{1}度の死を越えて、再び。|Awoke in {0}. Rising again after {1} deaths.
{0}との戦いに倒れた。|Fell in battle against {0}.
{0}を取り戻し、{1}を倒した。|Recovered {0} and defeated {1}.
{0}に捕らわれた人々を救った。|Rescued those held captive by {0}.
{0}を封じ、災いを鎮めた。|Sealed {0} and ended the calamity.
まもなく冒険が始まります（{0}秒） · Enterで観戦へ|Adventure starts in {0}s · Press Enter to watch
{0}との戦いで、力尽きた。|Fell in battle against {0}.
{0}日間の旅 · 討伐 {1} · 死亡 {2}回 · 踏破 {3}|{0} days · {1} defeated · {2} deaths · {3} explored
死亡 {0} 回|Deaths: {0}
{0} · {1}（敵Lv.{2}）· 踏破 {3}|{0} · {1} (Enemy Lv.{2}) · {3} explored
討伐 {0} · 回避 {1}|Defeated {0} · Dodged {1}
{0} 個 · {1} G|{0} items · {1} G
世界観：{0} ▾|Setting: {0} ▾
DotARPG — {0} · {1}|DotARPG — {0} · {1}
DotARPG — {0}|DotARPG — {0}
{0}  /  {1}|{0} / {1}
{0} · {1}　／　PLAYER {2}|{0} · {1} / PLAYER {2}
PLAYER  {0}|PLAYER  {0}
プレイ {0}回　クリア {1}回　死亡 {2}回　討伐 {3}体|{0} runs · {1} cleared · {2} deaths · {3} defeated
""";
}
