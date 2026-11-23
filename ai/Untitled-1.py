def countdown(n):
while n > 0:
n -= 1
yield n
temp=countdown(6)
print(next(temo))