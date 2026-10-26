"""Bounded chores through normal game interactions. Local configuration is never published."""
import argparse,json,math,time
from pathlib import Path
from control import send,state

class Agent:
    def __init__(self,world):
        self.sleeping=False
        self.opened_doors=set()
        send('arm',world=world)
    def check(self):
        s=state()
        if s.get('handoff'):raise RuntimeError('TAKE OVER: '+s['handoff'])
        if time.time()-__import__('datetime').datetime.fromisoformat(s['time'][:26]+'+00:00').timestamp()>5:raise RuntimeError('Game telemetry stopped')
        return s
    def call(self,action,**values):
        self.check()
        return send(action,**values)
    def wait(self,seconds):
        end=time.monotonic()+seconds
        while time.monotonic()<end:
            time.sleep(min(.5,end-time.monotonic()));self.call('heartbeat');self.sleep_if_night()
    def goto(self,point,within=1.5):
        p=self.check()['position']
        if math.hypot(point['x']-p['x'],point['z']-p['z'])<within+.25:self.call('stop');return
        for _ in range(30):
            result=self.call('route',x=point['x'],z=point['z'])
            if result.get('route'):break
            self.wait(.5)
        else:raise RuntimeError('No safe walking path found; take over to navigate')
        for attempt in range(4):
            route=result['route']
            try:
                for waypoint in route[1:-1]:self._walk(waypoint,.35,point,within)
                self._walk(point,within);return
            except RuntimeError as error:
                if 'Route blocked' not in str(error) or attempt==3:raise
                self.call('stop');self.wait(.6);result=self.call('route',x=point['x'],z=point['z'])
                if not result.get('route'):raise RuntimeError('No walking route around obstruction')
    def _walk(self,point,within=1.5,goal=None,radius=0):
        stalled=0
        for _ in range(160):
            self.sleep_if_night()
            s=self.check();p=s['position']
            if goal and math.hypot(goal['x']-p['x'],goal['z']-p['z'])<radius+.15:self.call('stop');return
            for door in s['objects']:
                if door['kind']!='door':continue
                v=door['position'];distance=math.hypot(v['x']-p['x'],v['z']-p['z'])
                ahead=(v['x']-p['x'])*(point['x']-p['x'])+(v['z']-p['z'])*(point['z']-p['z'])
                if distance<2.5 and not door['open'] and ahead>0:
                    self.call('door',target=door['id']);self.opened_doors.add(door['id']);self.wait(.8)
                elif door['id'] in self.opened_doors and door['open'] and 1.5<distance<2.8 and ahead<0:
                    self.call('door',target=door['id']);self.opened_doors.discard(door['id']);self.wait(.8)
            dx=point['x']-p['x'];dz=point['z']-p['z'];distance=math.hypot(dx,dz)
            if distance<within+min(.2,within*.3):self.call('stop');return
            self.call('move',x=dx,z=dz,seconds=max(.2,min(.6,(distance-within+.3)/3)));self.wait(.7)
            q=self.check()['position'];stalled=stalled+1 if math.hypot(q['x']-p['x'],q['z']-p['z'])<.12 else 0
            if stalled>=4:raise RuntimeError('Route blocked; take over to navigate')
        raise RuntimeError('Navigation limit exceeded')
    def sleep_if_night(self):
        s=self.check()
        if not s.get('sleepDue',s['night']) or self.sleeping:return
        self.sleeping=True;return_to=s['position']
        try:
            bed=next((o for o in s['objects'] if o['kind']=='bed' and o['mine']),None)
            if not bed:raise RuntimeError('Night: current bed is not loaded; take over')
            self.goto(bed['position'],2);self.call('sleep',target=bed['id'])
            for _ in range(120):
                self.wait(.5)
                if not self.check()['sleeping']:break
            else:raise RuntimeError('Sleep waiting for other players or dawn; take over')
            self.goto(return_to)
        finally:self.sleeping=False
    def count(self,item):return sum(i['amount'] for i in self.check()['inventory'] if i['item']==item)
    def transfer(self,chest,item,count,deposit):
        if count<=0:return
        self.goto(chest['position'],2);self.call('open',target=chest['id']);self.wait(.5)
        try:
            while count:
                s=self.check();inv=s['inventory'] if deposit else next(o for o in s['objects'] if o['id']==chest['id'])['inventory']
                stack=next((i for i in inv if i['item']==item),None)
                if not stack:raise RuntimeError('Missing transfer material '+item)
                amount=min(count,stack['amount']);self.call('transfer',target=chest['id'],item=item,amount=amount,deposit=deposit);count-=amount
        finally:self.call('close')
    def collect(self,items):
        for d in list(self.check()['drops']):
            if d['item'] not in items:continue
            self.goto(d['position'],1.5);self.wait(.5)
            if any(v['id']==d['id'] for v in self.check()['drops']):self.call('pickup',target=d['id']);self.wait(.3)
    def farm(self,ingredients):
        crops={'Carrot':('CarrotSeeds','sapling_carrot','sapling_seedcarrot'),'Turnip':('TurnipSeeds','sapling_turnip','sapling_seedturnip'),'Onion':('OnionSeeds','sapling_onion','sapling_seedonion')}
        allowed=set(crops)|{v[0] for v in crops.values()}|{'Barley','Flax'}
        soil={};seen=set()
        for _ in range(500):
            s=self.check()
            for v in s['soil'] or []:soil[(round(v['x'],1),round(v['z'],1))]=v
            ready=[v for v in s['nearby'] if v['item'] in allowed and v['id'] not in seen]
            if not ready:break
            target=ready[0];seen.add(target['id']);self.goto(target['position'],2);self.call('pick',target=target['id']);self.wait(.5);self.collect({target['item']})
        plans=[]
        for vegetable,(seed,plant,seedplant) in crops.items():
            plans.extend([(plant,seed)]*self.count(seed));plans.extend([(seedplant,vegetable)]*math.ceil(self.count(vegetable)/2))
        for item,prefab in [('Barley','sapling_barley'),('Flax','sapling_flax')]:plans.extend([(prefab,item)]*math.ceil(self.count(item)/2))
        occupied=[v['position'] for v in self.check()['plants']]
        for prefab,item in plans:
            self.sleep_if_night()
            points=[v for v in soil.values() if all(math.hypot(v['x']-o['x'],v['z']-o['z'])>=1.25 for o in occupied)]
            if not points:raise RuntimeError('Cultivated plot is full; remaining seeds retained')
            p=self.check()['position'];point=min(points,key=lambda v:math.hypot(v['x']-p['x'],v['z']-p['z']))
            while True:
                try:
                    self.goto(point,2);self.wait(.8)
                    self.call('plant',prefab=prefab,x=point['x'],z=point['z']);occupied.append(point);break
                except RuntimeError as error:
                    if 'Wait for stamina' in str(error):self.wait(2);continue
                    if not any(reason in str(error) for reason in ('placement validation rejected','Route blocked','No safe walking path','No walking route')):raise
                    occupied.append(point)
                    points=[v for v in soil.values() if all(math.hypot(v['x']-o['x'],v['z']-o['z'])>=1.25 for o in occupied)]
                    if not points:raise RuntimeError('No valid cultivated planting spaces remain')
                    point=min(points,key=lambda v:math.hypot(v['x']-self.check()['position']['x'],v['z']-self.check()['position']['z']))
        for item in list(crops)+['Barley','Flax']:self.transfer(ingredients,item,self.count(item),True)
    def smelt(self,config):
        # Roles must be surveyed in this session; never guess chest destinations.
        objects={o['id']:o for o in self.check()['objects']}
        role={k:objects[v] for k,v in config.items()}
        wood,coal,ore,ingots,kiln,smelter=[role[k] for k in ('wood','coal','ore','ingots','kiln','smelter')]
        conversions={v['input']:v['output'] for v in smelter['conversions']}
        materials=sorted((i for i in ore['inventory'] if i['item'] in conversions),key=lambda i:(i['item'] not in ('IronScrap','IronOre'),i['item']))
        units=[i['item'] for i in materials for _ in range(i['amount'])]
        for material in units:
            product=conversions[material]
            machine=next(o for o in self.check()['objects'] if o['id']==smelter['id'])
            needed=max(0,math.ceil(machine.get('fuelPerProduct',2)-float(machine['fuel'])))
            self.sleep_if_night()
            if not self.count(material):self.transfer(ore,material,1,False)
            self.collect({'Coal',product})
            if self.count('Coal')<needed:
                available=sum(i['amount'] for i in next(o for o in self.check()['objects'] if o['id']==coal['id'])['inventory'] if i['item']=='Coal')
                if available:self.transfer(coal,'Coal',min(needed-self.count('Coal'),available),False)
            if self.count('Coal')<needed:
                wood_needed=needed-self.count('Coal')
                self.transfer(wood,'Wood',wood_needed,False);self.goto(kiln['orePort'],2.5)
                for _ in range(wood_needed):self.call('ore',target=kiln['id'],item='Wood');self.wait(.2)
                for _ in range(180):
                    self.collect({'Coal'})
                    if self.count('Coal')>=needed:break
                    self.wait(1)
                else:raise RuntimeError('Kiln output timed out')
                self.transfer(coal,'Coal',self.count('Coal'),True);self.transfer(coal,'Coal',needed,False)
            self.goto(smelter['fuelPort'],2.5)
            for _ in range(needed):self.call('fuel',target=smelter['id'],item='Coal');self.wait(.2)
            self.goto(smelter['orePort'],2.5);self.call('ore',target=smelter['id'],item=material)
            for _ in range(180):
                self.collect({product})
                if self.count(product):break
                self.wait(1)
            else:raise RuntimeError('Iron output timed out')
            self.transfer(ingots,product,self.count(product),True)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('task',choices=['farm','smelt']);parser.add_argument('--world',required=True);parser.add_argument('--config',type=Path,required=True);args=parser.parse_args()
    agent=None
    try:
        config=json.loads(args.config.read_text());agent=Agent(args.world)
        if args.task=='farm':agent.farm(next(o for o in state()['objects'] if o['id']==config['ingredients']))
        else:agent.smelt(config)
        agent.call('save');print('Completed',args.task)
    finally:
        if agent:send('release')
if __name__=='__main__':main()
