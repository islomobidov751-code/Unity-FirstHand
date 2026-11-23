nums = [2, 7, 4, 8, 3]
result = sorted(
filter(lambda x: x % 2 != 0,
map(lambda x: x + 5, nums)),
reverse=True
)
print(result)
