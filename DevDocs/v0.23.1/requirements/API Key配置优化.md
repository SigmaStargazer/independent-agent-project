下面的需求用于优化API Key配置的用户体验

### 需求一

#### 背景

PanelLLMAgent、PanelLLMMemory可以配置相同的模型。因此提供一个复制按钮，将另一组配置的内容读取到当前panel的，可降低配置的工作量。

#### 具体需求

PanelLLMAgent上已有一个按钮：把 「大模型(记忆总结使用) 」 的配置复制到此处

相对应的，PanelLLMMemory上也已有一个按钮：把 「大模型(智能体使用) 」 的配置复制到此处

目前这两个按钮未绑定任何方法，请完成：
1）新增绑定在PanelLLMAgent上的UILLMAgent脚本，提供OnClickCopy：触发该方法后，可以读取LLMMemory的配置覆盖到PanelLLMAgent的三个文本框内

2）新增绑定在PanelLLMMemory上的UILLMMemory脚本，提供OnClickCopy：触发该方法后，可以读取LLMAgent的配置覆盖到PanelLLMMemory的三个文本框内

配置读到三个文本框后，后续用户还是通过按ESC跳出MsgboxSaveSetting的方式决定是否存储，这块不需要改变



### 需求二

#### 背景

API Key配置好并在MsgboxSaveSetting点「保存退出」后，目前需要进入到实际的场景才能验证API Key是否可用。如果配置有问题，还要重新点开设置一个个配。

最好的用户体验，是在MsgboxSaveSetting点「保存退出」后，立刻对刚刚的页面配置的API Key是否可用进行测试

**具体需求**

目前Title场景里又新增了3个Msgbox：

- MsgboxModelTesting
- MsgboxModelAvailable
- MsgboxModelUnavailable

具体唤起时机如下：
1）在MsgboxSaveSetting点「保存退出」后：

从当前的逻辑改为开始对API Key是否可用进行测试，并停留在当前Panel、关闭MsgboxSaveSetting、唤起MsgboxModelTesting

注意：刚刚的页面配置什么模型就测什么，不需要对所有模型都进行测试

2）当API Key可用性测试通过时：

关闭MsgboxModelTesting、唤起MsgboxModelAvailable

3）当API Key可用性测试未通过时：

关闭MsgboxModelTesting、唤起MsgboxModelUnavailable



三个Msgbox的按钮如下

1）MsgboxModelTesting

- 取消：可停止API Key可用性测试，并关闭该Msgbox

2）MsgboxModelAvailable

- 退出：关闭该Msgbox，并返回PanelSetting

2）MsgboxModelUnavailable

- 继续配置：关闭该Msgbox，留在当前Panel
- 退出：关闭该Msgbox，并返回PanelSetting

