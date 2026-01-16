直接kill掉占用kuzu文件的进程：

```
lsof | grep graphiti.kuzu
kill -9 <PID>
```

