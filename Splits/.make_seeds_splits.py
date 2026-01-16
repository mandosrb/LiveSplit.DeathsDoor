seeds, pots = {}, {}

with open('seeds.md') as f:
	for line in f:
		if line.startswith('####'):
			sline = line.rstrip().split()[1:]
			if 'seed' in sline:
				last_key = (' '.join(sline), 's')
				seeds[last_key[0]] = ''
			elif 'pot' in sline:
				last_key = (' '.join(sline), 'p')
				pots[last_key[0]] = ''
		elif line.startswith('!'):
			split_name = line.split(']')[0].split('[')[1]
			if last_key[1] == 's':
				seeds[last_key[0]] += split_name
			elif last_key[1] == 'p':
				pots[last_key[0]] += split_name

print('  <Parent name="Seeds" text="Seeds" tip="Splits on seed pickup">')
for txt, name in seeds.items():
	print(f'    <Split name="&B;{name}" text="{txt}"/>')
print('  </Parent>')

print('  <Parent name="Pots" text="Pots" tip="Splits when seed is planted">')
for txt, name in pots.items():
	print(f'    <Split name="&B;{name}" text="{txt}"/>')
print('  </Parent>')