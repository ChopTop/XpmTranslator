"""Shorten English labels of fixed-width WinUI buttons in pri_en.txt (matched by Chinese source)."""
import json, sys

sys.stdout.reconfigure(encoding='utf-8')
sp = sys.argv[1]
SHORT = {
    '立即更新': 'Update',
    '取消接收': 'Cancel',
    '取消传输': 'Cancel',
    '10分钟': '10 min',
    '30分钟': '30 min',
    '1小时': '1 h',
    '2小时': '2 h',
    '延后': 'Later',
    '取消更新': 'Cancel',
    '更新驱动': 'Update',
    '重新设置': 'Reset',
    '确认': 'OK',
    '下载隐私政策PDF': 'Download PDF',
    '重新录制': 'Redo',
    '取消录制': 'Cancel',
    '开始录制': 'Record',
    '立即重启': 'Restart',
}
todo = json.load(open(sp + '/tr/pri_todo.json', encoding='utf-8'))
idx = {t['zh']: i for i, t in enumerate(todo)}
path = sp + '/tr/pri_en.txt'
lines = open(path, encoding='utf-8').read().split('\n')
by_k = {l.partition(' ')[0]: n for n, l in enumerate(lines) if l}
for zh, en in SHORT.items():
    k = str(idx[zh])
    n = by_k[k]
    print(f'{k:>4} {zh} : {lines[n].partition(" ")[2]!r} -> {en!r}')
    lines[n] = f'{k} {en}'
open(path, 'w', encoding='utf-8').write('\n'.join(lines))
