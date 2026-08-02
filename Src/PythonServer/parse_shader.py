import json
import sys

with open(sys.argv[1], 'r', encoding='utf-8') as f:
    data = json.load(f)

# Build ID -> node info map
nodes = {}
# The JSON is a list of objects, first is GraphData, rest are node/slot definitions
obj_list = data if isinstance(data, list) else [data]

# Actually shadergraph files are a list of JSON objects concatenated
# Let's re-parse as a stream
