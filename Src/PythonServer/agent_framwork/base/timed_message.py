from dataclasses import dataclass

@dataclass(order=True)
class TimedMessage:
    timestamp: float
    content: str