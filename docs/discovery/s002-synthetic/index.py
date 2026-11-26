import sqlite3,json,time,os
print('sqlite',sqlite3.sqlite_version)
c=sqlite3.connect('pages.sqlite'); c.execute('drop table if exists pages'); c.execute("create virtual table pages using fts5(document_id UNINDEXED, page UNINDEXED, text, tokenize='unicode61 remove_diacritics 2')")
rows=[('doc-synthetic',x['Item1'],x['Item2']) for line in open('pages.jsonl') if (x:=json.loads(line))]
t=time.perf_counter(); c.executemany('insert into pages values(?,?,?)',rows); c.commit(); print('index_ms',round((time.perf_counter()-t)*1000,2),'rows',len(rows),'db_bytes',os.path.getsize('pages.sqlite'))
tests=[('constituicao',[1]),('CONSTITUIÇÃO',[1]),('"danos morais"',[2]),('indenizacao',[2]),('licitacao',[3]),('prescricao',[4]),('contestacao',[5]),('trabalhador',[2]),('trabalhadores',[2]),('indenizar',[2]),('"trabalho contrato"',[]),('"justica trabalhador"',[])]
for q,expected in tests:
 t=time.perf_counter(); hits=c.execute('select document_id,page from pages where pages match ? order by rank',(q,)).fetchall(); ms=(time.perf_counter()-t)*1000
 print(repr(q),'hits',hits[:8],'expected',expected,'pass', [int(x[1]) for x in hits]==expected,'ms',round(ms,3))
print('empty page',c.execute('select page,length(text) from pages where page=6').fetchall())
print('page 1 text',c.execute('select text from pages where page=1').fetchone()[0])
