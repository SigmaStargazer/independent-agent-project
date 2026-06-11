# Agent Prompt保存需求文档

## 问题及可能的解决思路

### 问题一：

目前agent_interuptible的所有message，仅在chatbot方法里通过message.pretty_print打印到终端。但是编辑器的终端打印内容有上限，可能会出现无法浏览到所有message的现象

### 解决思路：

需要在save_memory节点，顺带把本轮对话结束后的message都存入文件，供人类及AI开发者查阅

1）存储目录结构及命名

需要约定一个存储的根目录，然后以Agent的名字作为文件夹名，下面每个文件存储该Agent每一轮对话结束后的全套prompt，文件名为时间。即：

<存储目录名>/<Agent名>/<存储该轮对话时的时间>.<文件后缀>

（每轮对话分开存一个文件，主要是因为每一轮对话内的message可能会特别长，所以一轮对话存一个文件更容易查阅）

（.env里增加一个生成or开发的配置，仅设置为开发时进行存储）

2）存储格式

分析下存储为.txt和.log哪个更适合

3）存储的内容

建议还是按照chatbot中拼装并打印的prompts的方式，来重新拼装需要存储的内容：
```python
    name = state['name']
    mem_to_save = state['mem_to_save']

    cur_time = await TimeSystem().aget_current_time()
    prompt = await prompt_template.ainvoke({"messages": state['messages'],
                                     "name": state['name'],
                                     "curtime": cur_time,
                                     "mem_summary": state['mem_summary'],
                                     "mem_fact": state['mem_fact'],
                                     "mem_episode": state['mem_episode']})
    for message in prompt.messages:
        text = message.pretty_repr()   # 返回 str，不打印
        file.write(text + "\n\n")      # 写入文件
```

### 问题二：

目前agent_interuptible内的_filter_messages，其实是没有启用的。但是现在直接用在chatbot方法里的话，20的message上限感觉对于目前的整个项目来说不够用了。我现在其实不太想定死一个裁剪的上限，感觉这样定太高很容易爆max_tokens，定太低又会导致max_tokens吃不满。

请找到一个更加动态的裁剪*messages*的方案。要注意目前的需求仅涉及上下文裁剪，暂不考虑上下文压缩等方案
