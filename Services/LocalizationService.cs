using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnapPin.Services;

internal static class LocalizationService
{
    internal const string English = "English";
    internal const string SimplifiedChinese = "简体中文";
    internal const string TraditionalChinese = "繁體中文";

    private const uint TraditionalChineseMap = 0x04000000;
    private static bool _automaticLocalizationEnabled;

    private static readonly IReadOnlyDictionary<string, string> Simplified =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Application shell and dashboard
            ["SnapPin Preferences"] = "SnapPin 首选项",
            ["SnapPin History"] = "SnapPin 历史记录",
            ["SnapPin Capture"] = "SnapPin 截图",
            ["SnapPin Pin"] = "SnapPin 贴图",
            ["SnapPin Whiteboard"] = "SnapPin 白板",
            ["Capture it. Keep it in sight."] = "截图并置顶，随时查看。",
            ["A local-first screenshot and floating-reference tool for Windows."] = "本地优先的 Windows 截图与浮动参考工具。",
            ["Capture region  ·  Print Screen"] = "区域截图  ·  Print Screen",
            ["Pin clipboard  ·  F3"] = "贴出剪贴板  ·  F3",
            ["Overview"] = "概览",
            ["Pins"] = "贴图",
            ["Capture"] = "截图",
            ["Pin"] = "贴图",
            ["Control"] = "控制",
            ["Drag any region across one or more displays. Copy, save, or pin the result."] = "在一个或多个显示器上拖动选择区域，然后复制、保存或贴出结果。",
            ["Keep images, image files, text, or hex colors in borderless topmost windows."] = "将图片、图片文件、文字或十六进制颜色保留在无边框置顶窗口中。",
            ["Drag to move, scroll to resize, Ctrl+scroll for opacity, and right-click for tools."] = "拖动可移动，滚轮可缩放，Ctrl+滚轮调整透明度，右键打开工具。",
            ["Capture shortcut"] = "截图快捷键",
            ["Choose the key that starts region capture."] = "选择启动区域截图的按键。",
            ["Save shortcut"] = "保存快捷键",
            ["More settings"] = "更多设置",
            ["Alt+Shift+P restores click-through pins"] = "Alt+Shift+P 可恢复鼠标穿透的贴图",
            ["History"] = "历史记录",
            ["Group"] = "分组",
            ["Default"] = "默认",
            ["Add group"] = "添加分组",
            ["New group name"] = "新分组名称",
            ["Add"] = "添加",
            ["Remove"] = "移除",
            ["Delete current group"] = "删除当前分组",
            ["Close group"] = "关闭分组",
            ["Hide / show"] = "隐藏 / 显示",
            ["Link desktop"] = "关联桌面",
            ["Remove link"] = "取消关联",

            // Preferences
            ["General"] = "常规",
            ["Toolbar"] = "工具栏",
            ["Recording"] = "录制",
            ["Output"] = "输出",
            ["Hotkeys"] = "快捷键",
            ["OCR"] = "文字识别",
            ["About"] = "关于",
            ["Application"] = "应用程序",
            ["Interface language"] = "界面语言",
            ["Run SnapPin on system startup"] = "系统启动时运行 SnapPin",
            ["Always launch SnapPin as administrator"] = "始终以管理员身份启动 SnapPin",
            ["Applies the next time SnapPin starts. Windows will show a UAC confirmation prompt."] = "下次启动 SnapPin 时生效。Windows 将显示用户账户控制确认提示。",
            ["Back up open pins and settings automatically"] = "自动备份打开的贴图和设置",
            ["Keep SnapPin running in the notification area when the dashboard closes"] = "关闭主界面后仍在通知区域运行 SnapPin",
            ["Configuration storage"] = "配置存储",
            ["Settings are stored locally under your Windows user profile. No account or cloud connection is used."] = "设置保存在本机 Windows 用户配置中，不需要账户或云端连接。",
            ["Cancel"] = "取消",
            ["Save changes"] = "保存更改",
            ["Selection border width"] = "选择框边框宽度",
            ["Selection border color"] = "选择框边框颜色",
            ["Screen mask color (ARGB)"] = "屏幕遮罩颜色 (ARGB)",
            ["Show full-screen cross lines at the cursor"] = "在光标处显示全屏十字线",
            ["Show coordinate and size badge above the selection"] = "在选择区域上方显示坐标和尺寸",
            ["Show automatic window and UI-element detection outline"] = "显示自动窗口和界面元素检测轮廓",
            ["Show capture shortcut hints"] = "显示截图快捷键提示",
            ["Long capture frames"] = "长截图最大帧数",
            ["Scroll pause (ms)"] = "滚动暂停（毫秒）",
            ["Wheel clicks per frame"] = "每帧滚轮格数",
            ["Exclude SnapPin pins and controls from screenshots and recordings"] = "在截图和录屏中排除 SnapPin 贴图及控件",
            ["Excluded apps"] = "排除的应用",
            ["Exclude a running app"] = "排除正在运行的应用",
            ["Capture shortcuts"] = "截图快捷键提示",
            ["W / A / S / D     Move cursor precisely"] = "W / A / S / D     精确移动光标",
            ["Mouse wheel      Parent / child UI element"] = "鼠标滚轮      父级 / 子级界面元素",
            ["Shift + C            Recognize and copy text"] = "Shift + C            识别并复制文字",
            ["Tab                    Window / element mode"] = "Tab                    窗口 / 元素模式",
            ["L                        Long scrolling screenshot"] = "L                        长滚动截图",
            ["R                        Record selected region"] = "R                        录制选定区域",
            ["Tool size"] = "工具尺寸",
            ["Compact"] = "紧凑",
            ["Standard"] = "标准",
            ["Large"] = "大",
            ["Automatically follows Windows display scaling."] = "自动适配 Windows 显示缩放。",
            ["Capture toolbar"] = "截图工具栏",
            ["Drawing tools shown directly in the first capture row."] = "绘图工具直接显示在截图工具栏第一行。",
            ["Annotation toolbar"] = "标注工具栏",
            ["Output actions shown after the drawing tools."] = "输出操作显示在绘图工具之后。",
            ["Enable all"] = "全部启用",
            ["Move up"] = "上移",
            ["Move down"] = "下移",
            ["MP4 video (H.264)"] = "MP4 视频 (H.264)",
            ["Animated GIF"] = "动态 GIF",
            ["Capture mode"] = "录制模式",
            ["Selected region"] = "选定区域",
            ["Full screen"] = "全屏",
            ["MP4 quality"] = "MP4 质量",
            ["Compact · 24 fps · 3 Mbps"] = "紧凑 · 24 帧/秒 · 3 Mbps",
            ["Standard · 30 fps · 6 Mbps"] = "标准 · 30 帧/秒 · 6 Mbps",
            ["High · 60 fps · 10 Mbps"] = "高 · 60 帧/秒 · 10 Mbps",
            ["GIF frames per second"] = "GIF 每秒帧数",
            ["Countdown / max duration"] = "倒计时 / 最长时长",
            ["Countdown seconds"] = "倒计时秒数",
            ["Maximum duration seconds"] = "最长时长（秒）",
            ["GIF maximum width"] = "GIF 最大宽度",
            ["Include the mouse cursor"] = "包含鼠标光标",
            ["Highlight clicks"] = "突出显示点击位置",
            ["System audio"] = "系统声音",
            ["Microphone"] = "麦克风",
            ["System audio device"] = "系统声音设备",
            ["Microphone device"] = "麦克风设备",
            ["Recording folder"] = "录屏保存文件夹",
            ["Browse"] = "浏览",
            ["MP4 uses Windows Media Foundation and stays entirely on this computer."] = "MP4 使用 Windows Media Foundation，所有处理均在本机完成。",
            ["Show a shadow around pinned windows"] = "在贴图窗口周围显示阴影",
            ["Make new pinned images text-selectable by default (local OCR)"] = "新贴图默认可选择文字（本地 OCR）",
            ["Default opacity (%)"] = "默认不透明度 (%)",
            ["Maximum window size"] = "最大窗口尺寸",
            ["Fast thumbnail size"] = "快速缩略图尺寸",
            ["Pin groups"] = "贴图分组",
            ["Manage named groups from the Pins tab on the SnapPin dashboard"] = "在 SnapPin 主界面的“贴图”选项卡中管理命名分组",
            ["Default image format"] = "默认图片格式",
            ["Transparent"] = "透明",
            ["Light checkerboard"] = "浅色棋盘格",
            ["Dark checkerboard"] = "深色棋盘格",
            ["Pseudo-transparent"] = "伪透明",
            ["Switch bound pin groups automatically with Windows virtual desktops"] = "随 Windows 虚拟桌面自动切换已关联的贴图分组",
            ["Organize floating references into groups. A group can follow a Windows virtual desktop automatically."] = "将浮动参考内容整理到分组中，分组可自动跟随 Windows 虚拟桌面。",
            ["Maximum saved captures"] = "最多保存的截图数",
            ["Completed captures are stored locally. When the limit is reached, the oldest captures are removed automatically."] = "已完成的截图保存在本机。达到上限时会自动移除最旧的截图。",
            ["Naming and saving"] = "命名与保存",
            ["File name template"] = "文件名模板",
            ["Tokens: $yyyy  $MM  $dd  $HH  $mm  $ss"] = "变量：$yyyy  $MM  $dd  $HH  $mm  $ss",
            ["Output format"] = "输出格式",
            ["PNG (lossless)"] = "PNG（无损）",
            ["JPEG / WebP quality"] = "JPEG / WebP 质量",
            ["Export appearance"] = "导出外观",
            ["Border width"] = "边框宽度",
            ["Border color (ARGB)"] = "边框颜色 (ARGB)",
            ["Include a shadow when saving or copying as files"] = "保存或复制为文件时包含阴影",
            ["Shadow size"] = "阴影大小",
            ["Shadow color"] = "阴影颜色",
            ["Quick-save folder"] = "快速保存文件夹",
            ["Show a notification after quick save"] = "快速保存后显示通知",
            ["Automatically save every completed capture"] = "自动保存每次完成的截图",
            ["Automatic-save folder"] = "自动保存文件夹",
            ["Capture and copy"] = "截图并复制",
            ["Custom capture"] = "自定义截图",
            ["Draw on screen"] = "屏幕绘图",
            ["Pin clipboard"] = "贴出剪贴板",
            ["Hide/show all pins"] = "隐藏/显示全部贴图",
            ["Record region"] = "录制区域",
            ["Ignore shortcuts in app"] = "在应用中忽略快捷键",
            ["The emergency Alt+Shift+P shortcut always remains available so click-through pins can be recovered."] = "紧急快捷键 Alt+Shift+P 始终可用，以便恢复鼠标穿透的贴图。",
            ["OCR language"] = "文字识别语言",
            ["English + Chinese + Japanese"] = "英语 + 中文 + 日语",
            ["English + 简体 + 繁體"] = "英语 + 简体中文 + 繁體中文",
            ["English + 简体中文"] = "英语 + 简体中文",
            ["English + 繁體中文"] = "英语 + 繁體中文",
            ["English + Japanese"] = "英语 + 日语",
            ["Japanese"] = "日语",
            ["Private by design"] = "隐私优先设计",
            ["Text and QR/barcode recognition runs on this computer. Images are not uploaded to a cloud service."] = "文字与二维码/条码识别均在本机运行，图片不会上传到云端。",
            ["Local recognition"] = "本地识别",
            ["Automatically recognizes positioned text when a new pin opens. Each pin can still override this from its right-click menu."] = "打开新贴图时自动识别文字。每个贴图仍可在右键菜单中单独切换。",
            ["Local-first capture and floating visual references"] = "本地优先的截图与浮动视觉参考工具",
            ["This is an independent implementation built with documented Windows APIs. It contains no Snipaste code, branding, or assets."] = "这是使用公开 Windows API 构建的独立实现，不包含 Snipaste 的代码、品牌或资源。",
            ["Diagnostics and recovery"] = "诊断与恢复",
            ["Unexpected errors are logged locally. Logs are retained for 14 days and open pins use verified generation backups."] = "意外错误会记录在本机，日志保留 14 天；打开的贴图使用经过验证的多版本备份。",
            ["Open diagnostics folder"] = "打开诊断文件夹",
            ["Copy system summary"] = "复制系统摘要",
            ["GitHub updates"] = "GitHub 更新",
            ["Check for updates when SnapPin starts"] = "SnapPin 启动时检查更新",
            ["Updates are published on "] = "更新发布在 ",
            ["Check now"] = "立即检查",
            ["Version"] = "版本",
            ["Toolbar size"] = "工具栏尺寸",
            ["Covered in still screenshots"] = "在静态截图中覆盖",
            ["Windows lets SnapPin hide its own windows from video capture. Other applications are securely covered in still screenshots; video exclusion also requires privacy support from that application."] = "Windows 可让 SnapPin 在录屏中隐藏自己的窗口。其他应用会在静态截图中被安全覆盖；若要在录屏中排除，还需要该应用支持隐私保护。",
            ["Windows will display a User Account Control prompt when SnapPin starts"] = "SnapPin 启动时 Windows 将显示用户账户控制提示",
            ["Global shortcuts are ready"] = "全局快捷键已就绪",
            ["Repeat last region"] = "重复上次区域",
            ["Save as image"] = "另存为图片",
            ["Saved"] = "已保存",

            // Capture and annotation
            ["Rectangle or ellipse"] = "矩形或椭圆",
            ["Rectangle"] = "矩形",
            ["Arrow or line"] = "箭头或直线",
            ["Arrow"] = "箭头",
            ["Pencil"] = "铅笔",
            ["Marker"] = "标记笔",
            ["Blur brush"] = "模糊画笔",
            ["Text"] = "文字",
            ["Eraser"] = "橡皮擦",
            ["Eraser brush"] = "橡皮擦画笔",
            ["Magnifier"] = "放大镜",
            ["Undo (Ctrl+Z)"] = "撤销 (Ctrl+Z)",
            ["Redo (Ctrl+Y)"] = "重做 (Ctrl+Y)",
            ["Copy image"] = "复制图片",
            ["Save image"] = "保存图片",
            ["Quick save"] = "快速保存",
            ["Long scrolling capture"] = "长滚动截图",
            ["Screen recording"] = "屏幕录制",
            ["OCR / QR recognition"] = "文字 / 二维码识别",
            ["Cancel capture"] = "取消截图",
            ["Recognize text and QR/barcodes"] = "识别文字和二维码/条码",
            ["Record selected region (R)"] = "录制选定区域 (R)",
            ["Long scrolling screenshot (L)"] = "长滚动截图 (L)",
            ["Capture another area"] = "重新选择截图区域",
            ["Finish"] = "完成",
            ["Close"] = "关闭",
            ["Start capture"] = "开始截图",
            ["Set optional dimensions, ratio, delay, and cursor capture."] = "可设置尺寸、比例、延迟和是否包含光标。",
            ["Width (pixels)"] = "宽度（像素）",
            ["Height (pixels)"] = "高度（像素）",
            ["Use 0 for a freely drawn width"] = "设为 0 可自由绘制宽度",
            ["Use 0 for a freely drawn height"] = "设为 0 可自由绘制高度",
            ["Aspect ratio"] = "宽高比",
            ["Tip: leave width and height at 0 to constrain only the aspect ratio."] = "提示：宽度和高度都设为 0 时仅限制宽高比。",
            ["Delay (seconds)"] = "延迟（秒）",
            ["Mouse cursor"] = "鼠标光标",
            ["Cursor"] = "光标",
            ["Window"] = "窗口",
            ["Detected window"] = "检测到的窗口",
            ["Mode: UI element"] = "模式：界面元素",
            ["Mode"] = "模式",
            ["UI element"] = "界面元素",
            ["window"] = "窗口",
            ["Select area"] = "选择区域",
            ["Ellipse"] = "椭圆",
            ["Straight line"] = "直线",
            ["Arrow heads"] = "箭头样式",
            ["Solid or dashed outline"] = "实线或虚线轮廓",
            ["Stroke size"] = "线条粗细",
            ["Pencil size"] = "铅笔粗细",
            ["Marker size"] = "标记笔粗细",
            ["Blur brush size"] = "模糊画笔大小",
            ["Eraser size"] = "橡皮擦大小",
            ["Text outline"] = "文字轮廓",
            ["Text alignment"] = "文字对齐",
            ["Underline"] = "下划线",
            ["Strikethrough"] = "删除线",
            ["Apply crop"] = "应用裁剪",
            ["Cancel crop"] = "取消裁剪",
            ["Drag to choose the crop area"] = "拖动选择裁剪区域",
            ["Transparent background"] = "透明背景",

            // OCR, recording, history and pins
            ["Recognize text"] = "识别文字",
            ["Recognize text and scan QR/barcodes"] = "识别文字并扫描二维码/条码",
            ["Drag the smaller area to recognize"] = "拖动选择较小的识别区域",
            ["OCR results"] = "识别结果",
            ["Search recognized text"] = "搜索识别到的文字",
            ["Copy selected text"] = "复制所选文字",
            ["Copy all recognized text"] = "复制全部识别文字",
            ["Copy results"] = "复制结果",
            ["Copy"] = "复制",
            ["All text"] = "全部文字",
            ["Open recognized web link"] = "打开识别到的网页链接",
            ["Close recognition panel"] = "关闭识别面板",
            ["Ready"] = "就绪",
            ["Copied"] = "已复制",
            ["Pause"] = "暂停",
            ["Stop"] = "停止",
            ["Pause recording (Space)"] = "暂停录制（空格）",
            ["Stop and review recording (R or Esc)"] = "停止并检查录制（R 或 Esc）",
            ["Review and trim recording"] = "检查并裁剪录屏",
            ["Start"] = "开始",
            ["End"] = "结束",
            ["Play"] = "播放",
            ["Play or pause preview (Space)"] = "播放或暂停预览（空格）",
            ["Save trimmed"] = "保存裁剪后视频",
            ["Keep full"] = "保留完整视频",
            ["Discard"] = "丢弃",
            ["Recordings"] = "录屏",
            ["Images"] = "图片",
            ["Capture history"] = "截图历史记录",
            ["Search title, tag, text, or barcode"] = "搜索标题、标签、文字或条码",
            ["Search OCR text, barcode content, source, dimensions, or date"] = "搜索识别文字、条码内容、来源、尺寸或日期",
            ["Favourites only"] = "仅收藏",
            ["All sources"] = "所有来源",
            ["All types"] = "所有类型",
            ["Any date"] = "不限日期",
            ["Today"] = "今天",
            ["Last 7 days"] = "最近 7 天",
            ["Last 30 days"] = "最近 30 天",
            ["Show recycle bin"] = "显示回收站",
            ["Clear history"] = "清空历史记录",
            ["Recycle bin"] = "回收站",
            ["Empty recycle bin"] = "清空回收站",
            ["Pinned"] = "已贴出",
            ["Annotated"] = "已标注",
            ["Edited"] = "已编辑",
            ["With context"] = "包含上下文",
            ["Add or remove favourite"] = "添加或移除收藏",
            ["Restore"] = "恢复",
            ["Delete"] = "删除",
            ["Delete forever"] = "永久删除",
            ["Open folder"] = "打开文件夹",
            ["Name / tags"] = "名称 / 标签",
            ["Auto-copy"] = "自动复制",
            ["Layout"] = "版面",
            ["Retry"] = "重试",
            ["Pinned image"] = "贴图",
            ["Pin as thumbnail"] = "贴为缩略图",
            ["Show this pin"] = "显示此贴图",
            ["Close this pin"] = "关闭此贴图",
            ["click-through"] = "鼠标穿透",
            ["Free"] = "自由",
            ["Scrolling"] = "滚动中",
            ["Capture active window"] = "截取当前窗口",
            ["Disable hotkeys"] = "禁用快捷键",
            ["Hide/show all"] = "隐藏/显示全部",
            ["Restore click interaction"] = "恢复鼠标交互",
            ["Close all"] = "关闭全部",
            ["Switch group"] = "切换分组",
            ["Solo selected"] = "仅显示所选贴图",
            ["Capture history…"] = "截图历史记录…",
            ["Check for updates…"] = "检查更新…",
            ["Preferences…"] = "首选项…",
            ["Open SnapPin"] = "打开 SnapPin",
            ["Exit"] = "退出",
            ["One or more selected shortcuts are already used by Windows or another app"] = "一个或多个所选快捷键已被 Windows 或其他应用占用",
            ["{0} pins"] = "{0} 个贴图",
            ["1 pin"] = "1 个贴图",
            ["Virtual desktops unavailable"] = "虚拟桌面不可用",
            ["{0} · not bound"] = "{0} · 未关联",
            ["{0} · bound here"] = "{0} · 已关联到此处",
            ["Bound to {0}"] = "已关联到 {0}",
            ["Restart required"] = "需要重新启动",
            ["The interface language has changed. Restart SnapPin now to apply it throughout the app?\n\nChoose No to apply it the next time SnapPin starts."] = "界面语言已更改。是否立即重新启动 SnapPin，以在整个应用中应用新语言？\n\n选择“否”将在下次启动 SnapPin 时应用。",
            ["SnapPin could not restart automatically. Please close and open it again."] = "SnapPin 无法自动重新启动，请关闭后再次打开。",
            ["Open"] = "打开",
            ["Edit"] = "编辑",
            ["Show result"] = "显示结果",
            ["Show context"] = "显示上下文",
            ["1 saved item"] = "已保存 1 项",
            ["{0} saved items"] = "已保存 {0} 项",
            ["{0} of {1} saved items"] = "显示 {1} 项中的 {0} 项",
            ["Move this item to the recycle bin?"] = "将此项目移到回收站？",
            ["Permanently delete this item and its files?"] = "永久删除此项目及其文件？",
            ["Permanently delete every item in the recycle bin?"] = "永久删除回收站中的所有项目？",
            ["Move every active item to the recycle bin?"] = "将所有有效项目移到回收站？",
            ["Rename history item"] = "重命名历史项目",
            ["Name"] = "名称",
            ["Organize history item"] = "整理历史项目",
            ["Tags (comma separated)"] = "标签（用逗号分隔）",
            ["OCR text"] = "识别文字",
            ["Code"] = "代码",
            ["Editable layers"] = "可编辑图层",
            ["{0} recording · {1} frames"] = "{0} 录屏 · {1} 帧",
            ["Recognizing locally…"] = "正在本地识别…",
            ["No text or code was found."] = "未找到文字或代码。",
            ["Recognition cancelled."] = "已取消识别。",
            ["Recognition failed: {0}"] = "识别失败：{0}",
            ["{0} text characters"] = "{0} 个文字字符",
            ["code found"] = "已找到代码",
            ["{0} found"] = "已找到 {0}",
            ["saved with capture history"] = "已随截图历史记录保存",
            ["Paused"] = "已暂停",
            ["Saving…"] = "正在保存…",
            ["Trimming…"] = "正在裁剪…",
            ["Resume"] = "继续",
            ["Resuming…"] = "正在继续…",
            ["Discard this recording permanently?"] = "永久丢弃此录屏？",
            ["Discard recording"] = "丢弃录屏",
            ["Recording failed"] = "录屏失败",
            ["Long screenshot failed"] = "长截图失败",
            ["The recognition area was too small. Select an area and try again."] = "识别区域太小，请选择一个区域后重试。",
            ["Will save to: {0}"] = "将保存到：{0}",
            ["{0} frames"] = "{0} 帧",
            ["1 frame"] = "1 帧"
            , ["Text selectable (OCR)"] = "文字可选择（OCR）"
            , ["Copy unscaled image"] = "复制原始尺寸图片"
            , ["Save image as…"] = "图片另存为…"
            , ["Save unscaled image as…"] = "原始尺寸图片另存为…"
            , ["Show toolbar"] = "显示工具栏"
            , ["Set title...    F2"] = "设置标题...    F2"
            , ["Lock position    L"] = "锁定位置    L"
            , ["Window shade    Y"] = "窗口卷起    Y"
            , ["Snap to screen edges"] = "吸附到屏幕边缘"
            , ["Shadow"] = "阴影"
            , ["Zoom"] = "缩放"
            , ["Image processing"] = "图片处理"
            , ["Rotate left"] = "向左旋转"
            , ["Rotate right"] = "向右旋转"
            , ["Flip horizontally"] = "水平翻转"
            , ["Flip vertically"] = "垂直翻转"
            , ["Grayscale"] = "灰度"
            , ["Invert colors"] = "反转颜色"
            , ["Crop…"] = "裁剪…"
            , ["Paste"] = "粘贴"
            , ["Replace by file…"] = "用文件替换…"
            , ["Move to group"] = "移至分组"
            , ["View in folder"] = "在文件夹中查看"
            , ["More tools"] = "更多工具"
            , ["Recognize text / QR…"] = "识别文字 / 二维码…"
            , ["Fixed thumbnail"] = "固定缩略图"
            , ["Always on top"] = "始终置顶"
            , ["Make interactive"] = "恢复交互"
            , ["Enable click-through"] = "启用鼠标穿透"
            , ["Solo selected pins"] = "仅显示所选贴图"
            , ["Copy image as file"] = "复制图片文件"
            , ["Export selected images…"] = "导出所选图片…"
            , ["Print…    Ctrl+P"] = "打印…    Ctrl+P"
            , ["Refresh screenshot    F5"] = "刷新截图    F5"
            , ["Auto refresh"] = "自动刷新"
            , ["Off"] = "关闭"
            , ["Every {0} second"] = "每 {0} 秒"
            , ["Every {0} seconds"] = "每 {0} 秒"
            , ["Reset selected"] = "重置所选"
            , ["Close selected"] = "关闭所选"
            , ["Destroy"] = "销毁"
            , ["Zoom to {0}%"] = "缩放至 {0}%"
            , ["Refresh screenshot"] = "刷新截图"
            , ["Download and install it now?"] = "是否立即下载并安装？"
            , ["SnapPin update available"] = "SnapPin 有可用更新"
            , ["SnapPin update"] = "SnapPin 更新"
            , ["SnapPin {0} is installed. The official GitHub update source is unavailable."] = "已安装 SnapPin {0}，但官方 GitHub 更新源当前不可用。"
            , ["The official update source is not a valid HTTPS address."] = "官方更新源不是有效的 HTTPS 地址。"
            , ["The GitHub release did not contain a valid version."] = "GitHub 版本中没有有效的版本号。"
            , ["SnapPin {0} is up to date."] = "SnapPin {0} 已是最新版本。"
            , ["SnapPin {0} is available for this {1}."] = "此{1}可更新至 SnapPin {0}。"
            , ["portable copy"] = "便携版"
            , ["installed copy"] = "安装版"
            , ["See the GitHub release page for full release notes."] = "完整更新说明请查看 GitHub 发布页面。"
            , ["The required update package is not attached to this GitHub release."] = "此 GitHub 版本未附加所需的更新包。"
            , ["The diagnostic summary was copied."] = "诊断摘要已复制。"
            , ["SnapPin diagnostics"] = "SnapPin 诊断"
            , ["Update check failed: {0}"] = "检查更新失败：{0}"
            , ["GitHub update check failed: {0}"] = "GitHub 更新检查失败：{0}"
            , ["Version {0}"] = "版本 {0}"
            , ["Choose a SnapPin output folder"] = "选择 SnapPin 输出文件夹"
            , ["Select"] = "选择"
            , ["Callout"] = "标注框"
            , ["Number"] = "编号"
            , ["Blur"] = "模糊"
            , ["Click an annotation to select it, then drag to move it."] = "单击标注以选择，然后拖动进行移动。"
            , ["Click to place text. Enter adds a line; Ctrl+Enter finishes."] = "单击放置文字。Enter 换行；Ctrl+Enter 完成。"
            , ["Click the image, type the callout, then press Ctrl+Enter."] = "单击图片并输入标注内容，然后按 Ctrl+Enter 完成。"
            , ["Click the image to add the next numbered marker."] = "单击图片以添加下一个编号标记。"
            , ["Drag the eraser over only the pixels you want to remove."] = "用橡皮擦拖过需要移除的像素。"
            , ["Drag to paint blur directly where the brush passes."] = "拖动画笔，直接模糊经过的位置。"
            , ["Drag on the image to add {0}."] = "在图片上拖动以添加{0}。"
            , ["Bring to front"] = "置于顶层"
            , ["Bring forward"] = "上移一层"
            , ["Send backward"] = "下移一层"
            , ["Send to back"] = "置于底层"
            , ["Duplicate"] = "复制标注"
            , ["Capture region"] = "区域截图"
            , ["Draw"] = "绘图"
            , ["Save"] = "保存"
            , ["SnapPin long capture"] = "SnapPin 长截图"
            , ["Preparing long capture..."] = "正在准备长截图..."
            , ["Captured first frame"] = "已截取第一帧"
            , ["Duplicate frame ignored"] = "已忽略重复帧"
            , ["Unmatched frame ignored; retrying"] = "已忽略无法匹配的帧，正在重试"
            , ["Captured {0} frames"] = "已截取 {0} 帧"
            , ["{0}  |  kept {1}, skipped {2}"] = "{0}  |  保留 {1}，跳过 {2}"
            , ["Restart now"] = "立即重新启动"
            , ["Later"] = "稍后"
            , ["The interface language has changed. Restart SnapPin now to apply it throughout the app?\n\nChoose Later to apply it the next time SnapPin starts."] = "界面语言已更改。是否立即重新启动 SnapPin，以在整个应用中应用新语言？\n\n选择“稍后”将在下次启动 SnapPin 时应用。"
        };

    private static readonly IReadOnlyDictionary<string, string> TraditionalOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SnapPin 首选项"] = "SnapPin 偏好設定",
            ["常规"] = "一般",
            ["截图"] = "擷取",
            ["贴图"] = "釘選",
            ["工具栏"] = "工具列",
            ["快捷键"] = "快速鍵",
            ["文字识别"] = "文字辨識",
            ["保存更改"] = "儲存變更",
            ["界面语言"] = "介面語言",
            ["截图并置顶，随时查看。"] = "擷取並置頂，隨時查看。",
            ["本地优先的 Windows 截图与浮动参考工具。"] = "本機優先的 Windows 擷取與浮動參考工具。"
        };

    internal static string CurrentLanguage { get; private set; } = English;

    internal static string Normalize(string? language) => language is SimplifiedChinese or TraditionalChinese ? language : English;

    internal static void Configure(string? language) => CurrentLanguage = Normalize(language);

    internal static string Current(string text) => Translate(text, CurrentLanguage);

    internal static string Format(string format, params object?[] args) => string.Format(Current(format), args);

    internal static string Translate(string text, string? language)
    {
        if (string.IsNullOrEmpty(text) || Normalize(language) == English) return text;
        if (!Simplified.TryGetValue(text, out var translated)) return text;
        if (Normalize(language) == SimplifiedChinese) return translated;
        if (TraditionalOverrides.TryGetValue(translated, out var overridden)) return overridden;
        return ToTraditionalChinese(translated);
    }

    internal static void EnableAutomaticLocalization()
    {
        if (_automaticLocalizationEnabled) return;
        _automaticLocalizationEnabled = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply((DependencyObject)sender, CurrentLanguage)));
        EventManager.RegisterClassHandler(typeof(UserControl), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply((DependencyObject)sender, CurrentLanguage)));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((sender, _) => Apply((DependencyObject)sender, CurrentLanguage)));
    }

    internal static void Apply(DependencyObject root, string? language)
    {
        var normalized = Normalize(language);
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        ApplyTree(root, normalized, visited);
    }

    private static void ApplyTree(DependencyObject value, string language, ISet<DependencyObject> visited)
    {
        if (!visited.Add(value)) return;
        ApplyElement(value, language);

        foreach (var child in LogicalTreeHelper.GetChildren(value).OfType<DependencyObject>())
            ApplyTree(child, language, visited);

        var count = value is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(value) : 0;
        for (var index = 0; index < count; index++)
            ApplyTree(VisualTreeHelper.GetChild(value, index), language, visited);
    }

    private static void ApplyElement(DependencyObject value, string language)
    {
        if (value is Window window) window.Title = Translate(window.Title, language);

        if (value is TextBlock text && !BindingOperations.IsDataBound(text, TextBlock.TextProperty))
        {
            var topLevelInlines = text.Inlines.ToList();
            if (topLevelInlines.All(inline => inline is Run))
            {
                if (!string.IsNullOrWhiteSpace(text.Text)) text.Text = Translate(text.Text, language);
            }
            else
            {
                foreach (var run in DescendantRuns(text.Inlines).ToList())
                    if (!string.IsNullOrWhiteSpace(run.Text)) run.Text = Translate(run.Text, language);
            }
        }

        if (value is HeaderedContentControl headered &&
            !BindingOperations.IsDataBound(headered, HeaderedContentControl.HeaderProperty) && headered.Header is string header)
            headered.Header = Translate(header, language);
        if (value is HeaderedItemsControl headeredItems &&
            !BindingOperations.IsDataBound(headeredItems, HeaderedItemsControl.HeaderProperty) && headeredItems.Header is string itemsHeader)
            headeredItems.Header = Translate(itemsHeader, language);
        if (value is ContentControl content &&
            !BindingOperations.IsDataBound(content, ContentControl.ContentProperty) && content.Content is string label)
            content.Content = Translate(label, language);
        if (value is FrameworkElement element &&
            !BindingOperations.IsDataBound(element, FrameworkElement.ToolTipProperty) && element.ToolTip is string tip)
            element.ToolTip = Translate(tip, language);

        if (value is ListView { View: GridView grid })
            foreach (var column in grid.Columns)
                if (column.Header is string columnHeader) column.Header = Translate(columnHeader, language);
    }

    private static IEnumerable<Run> DescendantRuns(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return run;
            if (inline is Span span)
                foreach (var child in DescendantRuns(span.Inlines)) yield return child;
        }
    }

    private static string ToTraditionalChinese(string value)
    {
        var required = LCMapStringEx("zh-TW", TraditionalChineseMap, value, -1, null, 0,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (required <= 0) return value;
        var buffer = new StringBuilder(required);
        return LCMapStringEx("zh-TW", TraditionalChineseMap, value, -1, buffer, buffer.Capacity,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) > 0 ? buffer.ToString() : value;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string localeName, uint mapFlags, string source, int sourceLength,
        StringBuilder? destination, int destinationLength, IntPtr versionInformation, IntPtr reserved, IntPtr sortHandle);
}
