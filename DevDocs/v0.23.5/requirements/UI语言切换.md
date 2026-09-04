接着v0.23.4版本，把UI的语言切换给做了
### 切换位置

在PanelSetting-PanelTab-PanelTabs新增TabGameSettings。点击后切换显示ContentGameSettings，上面可以点击左右箭头切换 中文/English（默认中文）。数据模型共用GameSettingsModel



需要切换的内容：
1、Title场景的各文本、按钮上的文本

2、Bootstrap场景的进度条上方，会显示各FlowStep的DisplayName。这块也要能切换中英文。Bootstrap场景的Msgbox上的文本、按钮的文本也要切换

暂时先做这些内容的切换。我建议你先在该版本的文件夹下，先新写一个文件来完整盘点一下有哪些组件的哪些文本需要能切换，我好检查是否有遗漏

另外，各语言文本的配置建议用excel，这样策划好配置
