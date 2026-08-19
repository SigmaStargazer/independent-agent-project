目前Title Scene已对PanelConfig进行了更新。上面有4个按钮

- BtnLLMAgent
- BtnLLMMemory
- BtnEmbedding
- BtnReranker

并增加了4个Panel：

- PanelLLMAgent
- PanelLLMMemory
- PanelEmbedding
- PanelReranker

以及一个Msgbox：

- MsgboxSaveConfig

目前需要增加如下逻辑：

1）点击对应按钮时，打开对应Panel并关闭PanelConfig

2）在上面4个Panel处按“ESC”，弹出MsgboxSaveConfig

3）增加一个方法，可以关闭上面4个Panel，并打开PanelConfig

