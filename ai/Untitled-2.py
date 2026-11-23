class Countdown:
def __init__(self, n):
self.n = n
def __iter__(self):
return self
def __next__(self):
if self.n > 0:
self.n -= 1
return self.n
else:
raise StopIteration()
