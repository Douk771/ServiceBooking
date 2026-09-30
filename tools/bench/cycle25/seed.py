"""QA цикл 25, бенч §515: владелец, магазин, часы, товар и один заказ через публичный API; пишет seed25.json {tok, shop, oid}."""
import json,os,urllib.request,sys
B=os.environ.get("BENCH_BASE","http://localhost:5125")
def call(m,p,body=None,tok=None):
    h={"Content-Type":"application/json"}
    if tok:h["Authorization"]="Bearer "+tok
    r=urllib.request.Request(B+p,data=json.dumps(body).encode() if body is not None else None,headers=h,method=m)
    try:
        x=urllib.request.urlopen(r);t=x.read();return x.status,(json.loads(t) if t else None)
    except urllib.error.HTTPError as e:
        return e.code,e.read().decode()
docs={d["type"]:d["version"] for d in call("GET","/api/legal/documents")[1]["documents"]}
s,a=call("POST","/api/auth/register",{"firstName":"Qa","lastName":"Owner","phone":"+79990002525","password":"Password123!","legal":{"privacyAcknowledgedVersion":docs["Privacy"],"termsAcceptedVersion":docs["TermsClient"]}})
if s!=200: s,a=call("POST","/api/auth/login",{"phone":"+79990002525","password":"Password123!"})
tok=a["token"]
s,c=call("GET","/api/cities"); cid=c["items"][0]["id"]
s,sh=call("POST","/api/shops",{"name":"QA Shop","slug":"qa-shop-25","cityId":cid,"ownerTerms":{"version":docs["TermsOwner"]}},tok)
print("shop",s,str(sh)[:200]); tok=sh["token"]; shop=sh["shop"]["id"]
days=[{"dayOfWeek":d,"intervals":[{"start":"00:05","end":"00:00"}]} for d in ["Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"]]
print("hours",call("PUT",f"/api/shops/{shop}/working-hours",{"days":days},tok)[0])
print("pickup",call("PUT",f"/api/shops/{shop}/pickup-settings",{"asapEnabled":True,"scheduledEnabled":False,"slotStepMinutes":15,"preorderDays":0,"minPrepMinutes":0},tok)[0])
s,p=call("POST",f"/api/shops/{shop}/products",{"categoryId":None,"name":"Борщ","description":None,"unit":"Piece","price":300,"portionText":None,"weightStepGrams":None,"minQuantity":None,"isPublished":True,"stockOnHand":None},tok); print("prod",s,str(p)[:100])
s,o=call("POST",f"/api/storefront/qa-shop-25/orders",{"idempotencyKey":"11111111-1111-4111-8111-111111111111","items":[{"productId":p["id"],"quantity":1,"expectedUnitPrice":300}],"customerName":"Иван","customerPhone":"+79990003535","comment":None,"captchaToken":None})
print("order",s,str(o)[:150])
board=call("GET",f"/api/shops/{shop}/order-board",None,tok)[1]
oid=(board["newOrders"]+board["accepted"])[0]["id"]
json.dump({"tok":tok,"shop":shop,"oid":oid},open("seed25.json","w"))
print(open("seed25.json").read()[:80])
