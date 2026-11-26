from pathlib import Path
import zlib
p=Path('synthetic-2000.pdf')
texts={1:'A Constituição garante a proteção da ação judicial e o acesso à Justiça.',2:'O trabalhador requereu indenização por danos morais decorrentes de acidente de trabalho.',3:'O contrato administrativo prevê licitação e responsabilidade da administração pública.',4:'A prescrição tributária extingue o crédito fiscal após cinco anos.',5:'O réu apresentou contestação e pediu produção de provas no processo civil.'}
boiler='O presente documento contém fundamentos jurídicos e análise da legislação brasileira. '
objs=[]
def add(x): objs.append(x if isinstance(x,bytes) else x.encode('ascii')); return len(objs)
add('<< /Type /Catalog /Pages 2 0 R >>'); add(b''); add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>')
pages=[]
for n in range(1,2001):
    body = '' if n==6 else texts.get(n,boiler*10)
    def esc(s): return s.replace('\\','\\\\').replace('(','\\(').replace(')','\\)')
    lines=[body[i:i+95] for i in range(0,len(body),95)]
    content=('BT /F1 10 Tf 45 790 Td 14 TL '+ ' '.join('('+esc(x)+') Tj T*' for x in lines)+' ET').encode('cp1252')
    compressed=zlib.compress(content)
    cid=add(b'<< /Length %d /Filter /FlateDecode >>\nstream\n'%len(compressed)+compressed+b'\nendstream')
    pages.append(add(f'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {cid} 0 R >>'))
objs[1]=('<< /Type /Pages /Count 2000 /Kids ['+' '.join(f'{i} 0 R' for i in pages)+'] >>').encode()
out=bytearray(b'%PDF-1.4\n'); offsets=[0]
for i,o in enumerate(objs,1):
    offsets.append(len(out)); out.extend(f'{i} 0 obj\n'.encode()+o+b'\nendobj\n')
x=len(out); out.extend(f'xref\n0 {len(objs)+1}\n0000000000 65535 f \n'.encode())
for offset in offsets[1:]: out.extend(f'{offset:010} 00000 n \n'.encode())
out.extend(f'trailer\n<< /Size {len(objs)+1} /Root 1 0 R >>\nstartxref\n{x}\n%%EOF\n'.encode()); p.write_bytes(out)
print('pdf bytes',len(out),'pages',len(pages))
