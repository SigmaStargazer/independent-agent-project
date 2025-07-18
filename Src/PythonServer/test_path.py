import os

print(os.getcwd())
print(os.path.abspath(os.path.dirname(__file__)))
print(os.path.abspath(os.path.join(os.getcwd(), "..")))
print(os.path.join(os.path.abspath(os.path.join(os.getcwd(), "..")), "config"))
print(os.path.abspath(os.path.join(os.getcwd(), "../config")))
print(os.path.abspath(os.path.join(os.getcwd(), "..", "config")))