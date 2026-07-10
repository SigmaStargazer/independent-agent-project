from dataclasses import dataclass

@dataclass(order=True)
class TimedMessage:
    timestamp: float
    content: str
    skip_memory: bool = False
