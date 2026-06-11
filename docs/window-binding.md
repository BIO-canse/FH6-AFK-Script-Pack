# 窗口绑定与等比缩放

## 用户原话

```text
算了就改成绑定窗口，然后就是如果是分辨率在等比变动，那么画好的虚拟表格映射也会自动跟着变动，如果是非等比需要提示重新绑定，比如可以搞成小窗口放到副屏，然后主屏继续玩。
```

## 当前目标

窗口绑定同时解决坐标、截图基准和鼠标输入基准问题：

1. 启动确认后，脚本绑定当前前台的非脚本窗口，后续截图范围使用该窗口客户区，而不是整块屏幕。
2. 鼠标移动到右下角、屏幕中心、车辆格子右侧等动作不再移动真实鼠标，只更新输入层里的“虚拟鼠标坐标”。
3. 点击和滚轮默认通过绑定窗口的窗口消息直接发送到虚拟坐标，不再依赖真实鼠标所在位置。
4. 绑定成功后，输入层会安装低级鼠标钩子，屏蔽真实鼠标在目标窗口客户区内产生的物理鼠标消息；脚本自己发送的窗口消息和键盘急停不受影响。
5. 当前版本已撤回“重新抢前台 / 焦点保活”方案；真正后台焦点欺骗需要后续接入 VFH / Perception Map 这类进程内感知层。
6. 绑定成功后，叠加层窗口会直接覆盖 FH6 的 DWM 物理窗口矩形；绘制仍使用屏幕绝对坐标，但会按该窗口矩形左上角转换成本地坐标。
7. OCR 返回坐标仍然统一转换成屏幕绝对坐标，已有格子映射继续使用绝对坐标。
8. 这不是内存注入式完整后台焦点欺骗。键盘仍然是 Windows 全局 `SendInput`，因此 FH6 仍然需要保持可接收键盘输入；鼠标点击不再移动真实鼠标。
9. 截图、框选和叠加层使用 DWM 物理窗口矩形；窗口消息鼠标输入会把物理屏幕坐标按目标窗口 `GetClientRect` 尺寸缩放成消息坐标，避免混合 DPI 缩放下点击偏移。
10. 绑定成功后默认尝试把目标进程优先级设置为 `RealTime`，降低游戏掉帧或后台性能限制导致等待时间不足的概率；如果系统权限或游戏保护策略拒绝，程序会记录失败日志。需要改回高优先级或关闭时，可改 `target_process_priority`。

## 目标窗口查找

窗口绑定不再只依赖“当前前台窗口”。每次刷新绑定时会先枚举可见窗口，优先查找进程名或窗口标题符合配置的 FH6 / Forza 窗口；只有找不到目标窗口时，才回退到前台窗口。

默认配置：

```json
"target_window_process_keywords": [
  "forzahorizon6",
  "ForzaHorizon",
  "Forza",
  "FH6",
  "FH5"
],
"target_window_title_keywords": [
  "Forza Horizon 6",
  "Forza Horizon",
  "地平线"
]
```

排序规则：进程名命中优先于标题命中，同分时选择客户区面积更大的窗口。控制台、浏览器或启动器只要进程名/标题没有命中这些规则，就不会被当成目标 FH6 窗口。

## 鼠标输入配置

`config/default.json` 中默认启用：

```json
"use_window_message_mouse_input": true,
"block_physical_mouse_on_bound_window": true
```

- `use_window_message_mouse_input=true`：`InputController.MoveTo` 只记录虚拟坐标；`Click` 和滚轮向绑定窗口发送消息。
- `block_physical_mouse_on_bound_window=true`：绑定窗口后屏蔽真实鼠标在目标窗口客户区内的移动、点击和滚轮，避免真实鼠标悬停或误点影响菜单选择。
- 如果窗口消息点击在某个环境中完全不被游戏接受，可以临时关掉这两个开关做对照测试；默认发布包保持开启。

## 框选设置

首次框选完整可见车辆格子整体区域时，程序会尝试用框选区域中心点找到下面的游戏窗口，并保存当时的窗口客户区：

当前实现会先按目标窗口规则查找 FH6 / Forza 窗口。找到后，框选叠加层只覆盖该窗口的 DWM 物理矩形，并优先把这个目标窗口矩形保存为缩放基准；只有提前找不到目标窗口时，才回退到配置显示器和框选中心点识别窗口。这样可以避免主显示器和副显示器使用不同 DPI 缩放比例时，把另一个屏幕的虚拟坐标混入车辆格子坐标。

当前窗口基准不是 Win32 `ClientToScreen` 的逻辑客户区，而是 DWM 扩展边框物理矩形。原因是某些混合 DPI 场景下，FH6 的 `GetClientRect` / `ClientToScreen` 会返回缩放后的逻辑坐标，而截图和 OCR 使用的是物理像素坐标。

```text
calibration_client_left
calibration_client_top
calibration_client_width
calibration_client_height
```

已保存的车辆格子仍然是屏幕绝对坐标：

```text
grid_cell_left
grid_cell_top
grid_cell_width
grid_cell_height
```

运行时绑定目标窗口后，按当前客户区和框选时客户区的比例迁移：

```text
scaleX = current_client_width  / calibration_client_width
scaleY = current_client_height / calibration_client_height

new_grid_left   = current_client_left + (grid_cell_left - calibration_client_left) * scaleX
new_grid_top    = current_client_top  + (grid_cell_top  - calibration_client_top)  * scaleY
new_cell_width  = grid_cell_width  * scaleX
new_cell_height = grid_cell_height * scaleY
```

## 非等比处理

如果 `scaleX` 和 `scaleY` 差距超过容忍值，说明窗口比例已经变了。此时程序必须停止并提示删除或重设 `config/user-settings.json` 后重新框选，不能强行套旧格子。

## 旧设置

旧版 `user-settings.json` 没有窗口客户区基准时，程序仍然可以按旧绝对坐标运行，但无法自动等比迁移。需要迁移能力时，选择 `3` 重设设置重新框选。
