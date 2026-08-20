# 背景

v0.23这个大版本需要围绕着DevDocs/feature-design/打包方案.md来实现

目前这个小版本需要优先实现《打包方案.md》中第10章里原计划在v0.23.4 实现的“Title API 配置 UI 与注入”内容，以便于开展UI相关设计的美术工作



# 需求

1）在Title场景的PanelLLMAgent、PanelLLMMemory、PanelEmbedding、PanelReranker里的3个文本框，可以读取配置数据，显示在对应的文本框内

2）退出这4个panel时，如果配置的内容出现任何更改，会弹出MsgboxSaveConfig。

3）MsgboxSaveConfig有一个button绑定了UITitle的OnConfigSaveConfig。点击该按钮后可更新配置

4）如果模型相关的12条配置存在未配置项时，在UITitle的OnClickNewGame、OnClickContinueGame里，会弹出MsgboxNoApiKey而不是进入GameFlow



# 疑问

我发现kuzu需要用到的MEMORY_API、EMBEDDING_API、RERANKER_API，目前都是放在.env里的。

这块能改成读api_config.json吗？以及在游戏中更改了配置后，如何生效？
