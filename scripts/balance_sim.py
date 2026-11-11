#!/usr/bin/env python3
"""Simulador de combate 1x1 para balancear atributos do CORPO (GDD-016 §13).

Replica as fórmulas do jogo (HP, dano, armadura, esquiva, bloqueio, Estâmina, golpe
poderoso, reação e ticks) entre arquétipos de 20 pontos de corpo (STR/VIT/AGI) e
imprime a tabela de vitórias. Não simula movimento nem alcance: só o corpo a corpo.

  python3 scripts/balance_sim.py
Para testar números novos, passe parâmetros em mk(...) (nomes iguais aos do GameTuning).
"""
import random, itertools, statistics
def mk(**kw):
    p=dict(hp_base=5,hp_per_vit=3,dmg_per_str=2,armor_per_vit=1,block_per_vit=0.03,block_max=0.6,
           dodge_per_agi=0.02,dodge_react_per_agi=0.03,inst_base=3,inst_per=0.5,regen=2,
           cost_power=2,cost_react=2,power_bonus=3,move_base=2,move_per_agi=0.5,move_cap=6,
           power_mult=1.0,block_flat_per_vit=0.0,block_hardness=0,armor_pct=0.0,armor_pct_cap=0.6,weapon=5,wticks=2,dex=3,agi_regen=False,agi_regen_from=6,agi_regen_step=3,hit_base=0.9,hit_per_dex=0.03,crit_dex=0.02,var=0.15)
    p.update(kw); return p
def stats(b,P):
    s,v,a=b
    return dict(hp=P['hp_base']+P['hp_per_vit']*v, dmg=P['weapon']+P['dmg_per_str']*s, arm=P['armor_per_vit']*v, armp=min(P['armor_pct_cap'],P['armor_pct']*v),
        dodge=min(0.9,P['dodge_per_agi']*a), inst=max(1,round(P['inst_base']+P['inst_per']*(s+v))),
        blk=min(P['block_max'],P['block_per_vit']*v), blkflat=(P['block_flat_per_vit']*v+P['block_hardness']) if P['block_flat_per_vit']>0 else 0, rdodge=P['dodge_react_per_agi']*a, prefers_block=v>a,
        hit=min(1,P['hit_base']+P['hit_per_dex']*P['dex']), crit=P['crit_dex']*P['dex'],
        ticks=max(1,round(P['wticks']*10/(10+P['dex']))), ticks_pow=max(1,round((P['wticks']+1)*10/(10+P['dex']))),
        ini=a+P['dex'],
        regen=max(1,P['regen']-(max(0,a-P['agi_regen_from'])//P['agi_regen_step'] if P.get('agi_regen') else 0)))
def fight(A,B,P,rng,maxt=80):
    u=[dict(st=stats(A,P)),dict(st=stats(B,P))]
    for x in u:
        x['hp']=x['st']['hp']; x['inst']=x['st']['inst']; x['next']=rng.random()*0.01+(0 if True else 0); x['react']=0
    # iniciativa: quem tem mais AGI+DEX começa
    if u[1]['st']['ini']>u[0]['st']['ini']: u[1]['next']-=0.001
    else: u[0]['next']-=0.001
    for t in range(maxt):
        for x in u:   # regeneração: +1 a cada 'regen' ticks (por unidade, se a ficha trouxer 'regen')
            rg=x['st'].get('regen',P['regen'])
            if (t+1)%rg==0: x['inst']=min(x['st']['inst'],x['inst']+1)
        if t%4==0:
            for x in u: x['react']=1
        order=sorted([0,1],key=lambda i:u[i]['next'])
        for i in order:
            x=u[i]; y=u[1-i]
            if x['next']>t: continue
            if x['hp']<=0 or y['hp']<=0: continue
            pw = x['inst']>=P['cost_power']
            if pw: x['inst']-=P['cost_power']
            d=x['st']['dmg']*(P['power_mult'] if pw else 1)+(P['power_bonus'] if pw else 0)
            d+=rng.randint(-round(d*P['var']),round(d*P['var']))
            # reação do alvo
            blk=0; dd=0; blkf=0
            if y['react'] and y['inst']>=P['cost_react']:
                y['react']=0; y['inst']-=P['cost_react']; y['next']+=1
                if y['st']['prefers_block']: blk=y['st']['blk']; blkf=y['st']['blkflat']
                else: dd=y['st']['rdodge']
            hit=max(0,min(1,x['st']['hit']-y['st']['dodge']-dd))
            ticks = x['st']['ticks_pow'] if pw else x['st']['ticks']
            x['next']=t+ticks
            if rng.random()<hit:
                dmg=max(1,round(d*(1-y['st']['armp']))-y['st']['arm'])
                if rng.random()<x['st']['crit']: dmg=round(dmg*1.5)
                dmg=max(1,round(dmg*(1-blk))-blkf); y['hp']-=dmg
        if u[0]['hp']<=0 or u[1]['hp']<=0: break
    return (u[0]['hp']>0)-(u[1]['hp']>0), t
BUILDS={'Bruto 10/5/5':(10,5,5),'Tanque 5/10/5':(5,10,5),'Ágil 5/5/10':(5,5,10),'Equil 7/7/6':(7,7,6),
        'Ágil extremo 3/3/14':(3,3,14),'Bruto extremo 14/3/3':(14,3,3),'Tanque extremo 3/14/3':(3,14,3)}
def table(P,N=1500,seed=1):
    rng=random.Random(seed); names=list(BUILDS)
    print("%-22s"%""+"".join("%-9s"%n.split()[0][:8] for n in names)+" | média  TTK   Estâmina máx")
    for a in names:
        row=[];ttks=[]
        for b in names:
            w=0
            for _ in range(N):
                r,t=fight(BUILDS[a],BUILDS[b],P,rng); w+=(r+1)/2; ttks.append(t)
            row.append(w/N)
        s=stats(BUILDS[a],P)
        print("%-22s"%a+"".join("%-9.0f"%(100*x) for x in row)+" | %3.0f%%  %4.1f  %d"%(100*statistics.mean(row),statistics.mean(ttks),s['inst']))
if __name__=="__main__":
    print("== ATUAL (vitórias % da linha contra a coluna) =="); table(mk())
    P=mk(); 
    print("\nTiles por andada (atual 2+0.5*AGI, teto 6):",[min(P['move_cap'],round(P['move_base']+P['move_per_agi']*a)) for a in range(1,15)])
    print("Dodge por AGI (atual 2%):",[round(100*min(.9,.02*a)) for a in (4,6,8,10,14)])
