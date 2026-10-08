from PIL import Image
import os

d = 'c:/Users/15147/global-game-jam/art/anim/'
for n in ['player_idle', 'player_walk', 'enemy_bird_walk', 'enemy_beetle_walk', 'enemy_bug_walk', 'kaola_walk', 'enemy_dove_fly', 'beehome_idle', 'bee_walk', 'bullet_orb', 'muzzle_flash']:
    p = d + n + '.png'
    if not os.path.exists(p):
        print(n, 'MISSING')
        continue
    im = Image.open(p)
    print(f'{n:24s} {im.size}')
