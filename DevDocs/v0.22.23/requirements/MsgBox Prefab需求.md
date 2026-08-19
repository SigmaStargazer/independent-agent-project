目前好几个Scene里都有Msgbox，包括但不仅限于：

* Bootstrap Scene的MsgboxError
* Title Scene的MsgboxNewGame、MsgboxNoApiKey、MsgboxQuit
* Level0等Scene的MsgboxConfirmExit、MsgboxGameOver

这几个Msgbox有以下特点：

* 有一个WarningTxt，每个Msgbox上显示的文字不同
* 有1~2个按钮，每个按钮上显示的文字、触发的方法不同



目前有一个问题，就是我现在已经进入到做UI素材的阶段，我希望能将Msgbox给prefab化，这样只需要改一个Msgbox的外形和素材，就能同时调整所有Scene中的Msgbox

有什么方案吗？