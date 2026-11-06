"""Bounded opt-in execution test: exactly two one-MNQ Sim101 round trips.
Run only with current human authorization. No general cancel/flatten or live path.
"""
import sys
if "--execute-sim" not in sys.argv:
    raise SystemExit("Trading test disabled: requires --execute-sim and fresh human authorization; current mode is SCREEN ONLY")
import datetime
import json
import subprocess
import time
import uuid
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
CLIENT=ROOT/'.local'/'build'/'SimApiClient.exe'
RUN='sim-'+uuid.uuid4().hex[:12]
OUT=ROOT/'.local'/'evidence'/RUN
OUT.mkdir()
session=None
checks=[]
counter=0

def call(op='status', **kw):
    global counter
    counter+=1
    body=dict(op=op,account='Sim101',instrument='MNQ DEC26')
    if op!='status': body.update(session=session,requestId=f'{RUN}-{counter}')
    body.update(kw)
    result=subprocess.run([str(CLIENT),'json'],input=json.dumps(body),capture_output=True,text=True,encoding='utf-8',timeout=12)
    if result.returncode: raise RuntimeError(result.stderr)
    reply=json.loads(result.stdout)
    assert reply['account']=='Sim101' and reply['instrument']=='MNQ DEC26'
    with (OUT/'rpc.jsonl').open('a',encoding='utf-8') as f:
        f.write(json.dumps(dict(request=body,response=reply))+'\n')
    return reply

def check(ok, label):
    if not ok: raise AssertionError(label)
    checks.append(label);print('PASS',label,flush=True)

def wait(predicate, seconds=8):
    end=time.monotonic()+seconds
    while True:
        s=call()
        if predicate(s): return s
        if time.monotonic()>end: raise AssertionError('State timeout: '+json.dumps(s))
        time.sleep(.25)

def empty(s): return s['position']['quantity']==0 and not s['orders']

def ready():
    s=call();check(empty(s),'flat MNQ Sim101 with no pending orders before entry')
    if not s['panel']['armed']:
        check(call('arm')['ok'],'flat SIM protection arm accepted')
        s=wait(lambda s:s['panel']['armed'])
    return s

def cleanup():
    s=call()
    if s['position']['quantity']:
        result=call('close_test')
        if not result['ok']: raise RuntimeError('Cannot clean owned test: '+json.dumps(result))
    return wait(empty)

try:
    initial=call();session=initial['session'];check(empty(initial),'initial Sim101 empty')
    for side in ('BUY','SELL'):
        ready()
        check(call('preview_entry',side=side,quantity=1)['ok'],side+' preview valid')
        refused=call('place_entry',side=side,quantity=1)
        check(not refused['ok'] and 'confirm' in refused['message'],side+' missing confirmation sends no order')
        check(empty(call()),'preview and missing-confirm test leave account empty')
        request_id=f'{RUN}-entry-{side}'
        submitted=call('place_entry',side=side,quantity=1,confirm='SIM101_ONLY',requestId=request_id)
        check(submitted['ok'],side+' market entry accepted')
        try:
            filled=wait(lambda s:s['position']['quantity']==1)
            check(filled['position']['side']==('Long' if side=='BUY' else 'Short'),side+' filled one MNQ in expected direction')
            duplicate=call('place_entry',side=side,quantity=1,confirm='SIM101_ONLY',requestId=request_id)
            check(duplicate==submitted,side+' identical write replay returns same result')
            protected=wait(lambda s:any(o['name']=='MNQPanel.Stop' and o['state'] in ('Accepted','Working') for o in s['orders']))
            stop=next(o for o in protected['orders'] if o['name']=='MNQPanel.Stop')
            avg=protected['position']['average'];direction=1 if side=='BUY' else -1
            planned=direction*(avg-stop['stop'])*2+2
            check(stop['quantity']==1 and stop['type']=='StopMarket' and stop['action']==('Sell' if side=='BUY' else 'BuyToCover'),side+' active opposite one-contract StopMarket')
            check(0<planned<=300.001,side+' stop budget includes $2 round-trip fee and is <= $300')
            (OUT/f'{side}-protected.json').write_text(json.dumps(protected,indent=2),encoding='utf-8')
            check(call()['position']['quantity']==1,side+' replay caused no duplicate position')
            refused=call('place_entry',side=side,quantity=1,confirm='SIM101_ONLY')
            check(not refused['ok'] and 'SIM_NOT_EMPTY' in refused['message'],side+' second entry into occupied position rejected')
            for operation in ('change_stop','cancel_order','breakeven','place_limit','place_stop','partial_close'):
                denied=call(operation)
                check(not denied['ok'] and denied['message']=='OPERATION_DENIED',operation+' explicitly unsupported')
            close_id=f'{RUN}-close-{side}'
            closed=call('close_test',requestId=close_id)
            check(closed['ok'],side+' owned market close accepted')
            check(call('close_test',requestId=close_id)==closed,side+' close replay idempotent')
            end=wait(empty)
            check(empty(end),side+' position flat and protective orders removed')
            time.sleep(1)
            check(empty(call()),side+' remains flat without orphan orders')
        finally:
            cleanup()
    final=call()
    (OUT/'result.json').write_text(json.dumps(dict(ok=True,checks=checks,final=final),indent=2),encoding='utf-8')
    print('REPORT',OUT,flush=True)
except Exception as exc:
    (OUT/'result.json').write_text(json.dumps(dict(ok=False,error=str(exc),checks=checks),indent=2),encoding='utf-8')
    print('FAILED',str(exc),'REPORT',OUT,flush=True)
    raise
