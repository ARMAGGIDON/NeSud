"use strict";
const $=id=>document.getElementById(id), levels=["Легка","Середня","Складна","Експерт"];
let daily=null, dailyBusy=false, revealed=false, resultKey="", comparison=null, reviveEnd=0, previousGame=null;
let clockAnchor=performance.now(), pulse=null, dailyTask=null, dailyMode="win";
let autoFinish=localStorage.getItem("autoFinish")==="1";
let adPending=false, adClock=null, adKind="", adRemaining=8, adBusy=false, nextGuest=0;
let token=sessionStorage.getItem("token")||"", guestMode=sessionStorage.getItem("guest")==="1", registration=false, authBusy=false, game=null, selected=0, note=false, online=false, busy=false, tab="play", epoch=0;
const message=s=>$("status").textContent=s;
if(localStorage.getItem("theme")==="light")document.documentElement.classList.add("light");
$("theme").onclick=()=>{document.documentElement.classList.toggle("light");localStorage.setItem("theme",document.documentElement.classList.contains("light")?"light":"dark")};
async function api(path,body){
 const controller=new AbortController(), timeout=setTimeout(()=>controller.abort(),path==='web/new'?30000:5000);
 try{const r=await fetch("/api/"+path,{method:body===undefined?"GET":"POST",headers:{"Content-Type":"application/json","Authorization":"Bearer "+token},body:body===undefined?undefined:JSON.stringify(body),signal:controller.signal});
 if(r.status===401&&path!=="login"){token="";sessionStorage.removeItem("token");sessionStorage.removeItem("guest");resetSession();throw Error("Сеанс завершено. Відновлюємо демо.");}
 if(!r.ok){const problem=await r.json().catch(()=>({}));throw Object.assign(Error(problem.error||problem.detail||(r.status===409?"Сеанс змінено в іншій вкладці.":r.status===429?"Забагато спроб. Спробуйте через хвилину.":"Сервіс тимчасово недоступний.")),{status:r.status});}
 return await r.json().catch(()=>null);
 }finally{clearTimeout(timeout)}
}
function resetSession(){daily=null;revealed=false;if($("daily").open)$("daily").close();epoch++;game=null;online=false;guestMode=true;selected=0;note=false;adPending=false;if($("ad").open)$("ad").close();$("account").hidden=false;$("logout").hidden=true;$("history-tab").hidden=true;$("guest-notice").hidden=false;render()}
async function pauseForNavigation(){if(pulse)await pulse;if(busy)return false;if(game&&!game.paused&&!game.completed&&!game.outOfLives&&online)return await act("pause");return true}
async function showAuth(){if(!await pauseForNavigation())return;mode(false);authError("");if(!$("auth").open)$("auth").showModal()}
$("account").onclick=showAuth;$("auth-close").onclick=()=>{$("auth").close();$("password").value=$("confirmation").value=""};
$("auth").addEventListener("close",()=>{$("password").value=$("confirmation").value=""});
function authError(text){$("auth-error").textContent=text;$("auth-error").hidden=!text}
function mode(register){registration=register;$("auth-title").textContent=register?"Створення акаунта":"Вхід до акаунта";$("confirmation-wrap").hidden=!register;$("confirmation").required=register;$("confirmation").value="";$("password").autocomplete=register?"new-password":"current-password";$("submit-auth").textContent=register?"Зареєструватися":"Увійти";$("register-tab").classList.toggle("active",register);$("login-tab").classList.toggle("active",!register);authError("")}
$("login-tab").onclick=()=>mode(false);$("register-tab").onclick=()=>mode(true);
document.querySelectorAll("[data-secret]").forEach(b=>b.onclick=()=>{const input=$(b.dataset.secret),show=input.type==="password";input.type=show?"text":"password";b.textContent=show?"Приховати":"Показати";b.setAttribute("aria-pressed",String(show));b.setAttribute("aria-label",b.textContent+" пароль")});
async function login(guest=false){
 if(authBusy||busy)return;authError("");if(guest)nextGuest=Date.now()+15000;
 if(!guest&&registration&&$("password").value!==$("confirmation").value){authError("Паролі не збігаються.");$("confirmation").focus();return}
 authBusy=true;$("submit-auth").disabled=$("guest").disabled=$("login-tab").disabled=$("register-tab").disabled=true;
 try{if(guest&&token&&guestMode){if($("auth").open)$("auth").close();return}
 const oldToken=token, oldGuest=guestMode;
 const a=await api(guest?"guest":registration?"register":"login",guest?{}:{user:$("user").value,password:$("password").value,confirmation:registration?$("confirmation").value:null});
 daily=null;epoch++;game=null;selected=0;note=false;token=a.token;guestMode=guest;sessionStorage.setItem("token",token);sessionStorage.setItem("guest",guest?"1":"0");$("password").value=$("confirmation").value="";if($("auth").open)$("auth").close();
 if(oldGuest&&oldToken&&oldToken!==token)fetch("/api/logout",{method:"POST",headers:{Authorization:"Bearer "+oldToken}}).catch(()=>{});
 await start();}
 catch(e){const error=e.name==="AbortError"||e instanceof TypeError?"Не вдалося з’єднатися з сервісом. Спробуйте ще раз.":e.message;if(guest)message(error);else authError(error)}
 finally{authBusy=false;$("submit-auth").disabled=$("guest").disabled=$("login-tab").disabled=$("register-tab").disabled=false}
}
$("login").onsubmit=e=>{e.preventDefault();login()};$("guest").onclick=()=>login(true);
$("logout").onclick=async()=>{if(busy||authBusy)return;try{if(game&&!game.paused&&!game.completed)await act("pause");await api("logout",{})}catch{}token="";sessionStorage.removeItem("token");sessionStorage.removeItem("guest");resetSession();message("");await login(true)};
async function start(){$("app").hidden=false;$("account").hidden=!guestMode;$("logout").hidden=guestMode;$("guest-notice").hidden=!guestMode;$("history-tab").hidden=guestMode;await navigate("play");await reconnect();if(!guestMode)refreshDaily()}
function accept(data){
 previousGame=game;game=data?.empty?null:data;online=navigator.onLine;clockAnchor=performance.now();
 if(game){selected=Math.max(0,Math.min(80,selected));reviveEnd=performance.now()+(game.reviveRemainingMs||0);
 if(previousGame?.id!==game.id){resultKey="";comparison=null;$("result-difficulty").value=String(game.difficulty)}
 if(game.completed||game.finalDefeat)loadComparison();
 }
 render();
 if(game&&previousGame?.id===game.id){
 for(let i=0;i<81;i++)if(game.values[i]!==previousGame.values[i]&&game.values[i])animate(buttons[i],game.wrong[i]?"shake":"pop");
 if(game.lives<previousGame.lives)animate($("lives"),"heart-loss");
 }
}
async function reconnect(){
 if(busy||!token)return;const e=epoch;busy=true;
 try{const data=await api("web/session");if(e!==epoch)return;accept(data);if(game?.reward&&!$("ad").open){const cleared=await api("web/action",{id:game.id,revision:game.revision,action:"reward-cancel"});if(e!==epoch)return;accept(cleared);}if(game&&!game.paused&&!game.completed&&!game.outOfLives){const paused=await api("web/action",{id:game.id,revision:game.revision,action:"pause"});if(e!==epoch)return;accept(paused);}message("");}
 catch(e){if(token)disconnect(e);else authError(e.message)}finally{busy=false;render()}
}
function disconnect(e){online=false;message("Гру призупинено: "+e.message+". Очікуємо з’єднання.");render()}
window.addEventListener("offline",()=>disconnect(Error("немає мережі")));
window.addEventListener("online",()=>token?reconnect():login(true));
async function act(action,extra={}){
 if(action!=="heartbeat"&&pulse)await pulse;
 if(!online||busy||!game)return false;const e=epoch;busy=true;
 try{const data=await api("web/action",{id:game.id,revision:game.revision,action,...extra});if(e!==epoch)return;if(data.pulseOnly){game={...game,...data};clockAnchor=performance.now();updateClock();return true}accept(data);if(action==="hint"){selected=game.selected;animate(buttons[selected],"hint-pulse")}if(game.completed&&!previousGame?.completed&&!guestMode)refreshDaily();return true;}
 catch(e){if(e.status===400||e.status===409&&action==="wallet")message(e.message);else disconnect(e);return false}finally{busy=false;if(action!=="heartbeat"||!online||game?.outOfLives||game?.paused||game?.completed||game?.reward)render()}
}
setInterval(()=>{if(busy||authBusy||adBusy||pulse)return;if(!token){if(navigator.onLine&&!$("auth").open&&Date.now()>=nextGuest)login(true);return}if(!online){reconnect();return}if(game&&!game.completed&&!game.finalDefeat&&(!game.paused||game.reward||game.outOfLives)){pulse=act("heartbeat").finally(()=>{pulse=null})}},6000);
setInterval(()=>{if(online&&(tab==="ranking"||tab==="weekly"))navigate(tab)},30000);
function confirmNew(){return new Promise(resolve=>{const d=$("confirm");d.querySelector("p").textContent=guestMode?"Поточну демонстраційну гру буде замінено.":"Поточна незавершена гра залишиться в історії.";d.returnValue="cancel";d.addEventListener("close",()=>resolve(d.returnValue==="yes"),{once:true});d.showModal()})}
$("newgame").onclick=async()=>{if(pulse)await pulse;if(!online||busy||adPending)return;if(game&&!game.completed&&!game.finalDefeat&&!(await confirmNew()))return;if(!online||busy)return;const e=epoch;busy=true;render();message("Генерація головоломки…");
 try{const data=await api("web/new",{difficulty:Number($("difficulty").value),id:game?.id??null,revision:game?.revision??0,autoFinish});if(e!==epoch)return;selected=data.selected;accept(data);message("");}
 catch(e){disconnect(e)}finally{busy=false;render()}};
$("resume").onclick=()=>!online?(token?reconnect():login(true)):game?.outOfLives?(game.reviveSeconds>0?reward("life"):$("newgame").onclick()):act("resume");
const buttons=[];for(let i=0;i<81;i++){let b=document.createElement("button");b.className="cell";b.onclick=()=>{selected=i;render();b.focus()};$("board").append(b);buttons.push(b)}
for(let d=1;d<=9;d++){const b=document.createElement("button");const num=document.createElement("strong"),count=document.createElement("small");num.textContent=d;count.textContent="9";b.append(num,count);b.onclick=()=>enter(d);$("pad").append(b)}
function enter(d,temp=false){act("enter",{cell:selected,digit:d,note:temp||note})}
$("notes").onclick=()=>{note=!note;render()};
function tool(action){if(action==="hint"&&game?.hintCredits===0)return chooseResource("hint");if(action==="candidates"&&game?.candidateCredits===0)return chooseResource("candidates");return act(action)}
document.querySelectorAll("[data-action]").forEach(b=>b.onclick=()=>b.dataset.action==="erase"?enter(0):tool(b.dataset.action));
const rewardNames={life:"Одне життя",hint:"Одна підказка",candidates:"Одне використання кандидатів"};
const resourceNames={life:"Життя",hint:"Підказки",candidates:"Кандидати"};
const balance=k=>daily?.[k==="life"?"lives":k==="hint"?"hints":"candidates"]||0;
async function refreshDaily(){
 if(guestMode||!token||dailyBusy||!online)return;if(dailyTask)return dailyTask;
 const e=epoch;dailyTask=(async()=>{try{const d=await api("daily");if(e!==epoch)return;daily=d;render();if($("daily").open&&!revealed)drawDaily()}catch{}finally{dailyTask=null}})();return dailyTask;
}
async function chooseResource(kind){
 if(!guestMode){await refreshDaily();if(balance(kind)>0){await spend(kind);return}}
 await reward(kind)
}
async function spend(kind){
 if(busy||dailyBusy||!game||!online||guestMode)return;
 if(await act("wallet",{reward:kind})){message("");await refreshDaily()}
}
function drawDaily(){
 $("daily-summary").textContent=daily?"Сьогодні: "+daily.wins+"/7 перемог · доступно карток: "+daily.packs.length:"Підключення…";
 const wallet=$("wallet"),cards=$("daily-cards");wallet.replaceChildren();cards.replaceChildren();
 if(!daily)return;
 for(const kind of ["hint","candidates"]){const b=document.createElement("button");b.textContent=resourceNames[kind]+": "+balance(kind)+" · використати";const count=kind==="life"?game?.lives:kind==="hint"?game?.hintCredits:game?.candidateCredits;b.disabled=!game||game.completed||count>=3||!balance(kind)||dailyBusy;b.onclick=async()=>{b.disabled=true;await spend(kind);drawDaily()};wallet.append(b)}
 const pack=daily.packs.find(p=>dailyMode==="weekly"?p.milestone==="weekly":p.milestone!=="weekly");$("daily-milestone").textContent=!pack?"Усі доступні нагороди отримано.":({login:"Попередня нагорода за вхід",win1:"Перша перемога",win3:"Третя перемога",win7:"Сьома перемога",win:"Перемога "+(pack.slot+1),weekly:"Тижневий турнір"})[pack.milestone]+" · "+pack.day;
 if(!pack)return;
 for(let i=0;i<3;i++){const b=document.createElement("button");b.className="mystery-card";b.textContent="?";b.setAttribute("aria-label","Вибрати картку "+(i+1));b.disabled=dailyBusy;b.onclick=()=>chooseCard(pack,i);cards.append(b)}
}
async function chooseCard(pack,card){
 if(dailyBusy)return;dailyBusy=true;drawDaily();
 try{const r=await api("daily/choose",{...pack,card});daily=r.status;revealed=true;const cards=$("daily-cards");Array.from(cards.children).forEach((b,i)=>{b.textContent=i===card?resourceNames[r.item.kind]+" ×"+r.item.amount:"?";b.classList.toggle("revealed",i===card);b.disabled=true});$("daily-result").textContent="Отримано: "+resourceNames[r.item.kind]+" ×"+r.item.amount;const next=document.createElement("button");next.className="primary next-card";next.textContent="Далі";next.onclick=()=>{revealed=false;drawDaily()};cards.append(next);}
 catch(e){$("daily-result").textContent=e.message}
 finally{dailyBusy=false;if(!revealed)drawDaily();render()}
}
$("daily-open").onclick=async()=>{if(guestMode){showAuth();return}if(dailyMode!=="weekly"&&!game?.completed)return;if(!await pauseForNavigation())return;revealed=false;$("daily-result").textContent="";$("daily").showModal();drawDaily();await refreshDaily()};
$("daily-close").onclick=()=>$("daily").close();

function adRender(){$("ad-time").textContent=adRemaining>0?adRemaining+" с":"Готово!";$("ad-progress").value=Math.max(0,8-adRemaining);$("ad-claim").disabled=adRemaining>0||adBusy||busy||!online}
async function reward(kind){
 if(pulse)await pulse;if(!online||busy||adPending||!game||game.completed||kind!=="life"&&((kind==="hint"?game.hintCredits:game.candidateCredits)>0||balance(kind)>0)||game.outOfLives&&kind!=="life"||kind==="life"&&(!game.outOfLives||!game.reviveSeconds))return;adPending=true;
 if(game.reward&&game.reward.kind!==kind){if(!await act("reward-cancel")){adPending=false;return}}
 if(!await act("reward-start",{reward:kind})){adPending=false;render();return}
 adKind=kind;adRemaining=game.reward?.remaining??8;$("ad-kind").textContent=rewardNames[kind];
 const deadline=performance.now()+adRemaining*1000;adRender();$("ad").showModal();
 adClock=setInterval(()=>{adRemaining=Math.max(0,Math.ceil((deadline-performance.now())/1000));adRender()},100);
}
$("ad-claim").onclick=async()=>{if(adRemaining>0||adBusy||busy)return;adBusy=true;adRender();const ok=await act("reward-claim",{reward:adKind});adBusy=false;if(ok){message("");$("ad").close()}else adRender()};
$("ad-cancel").onclick=()=>$("ad").close();
$("ad").addEventListener("cancel",e=>{if(adBusy)e.preventDefault()});
$("ad").addEventListener("close",async()=>{clearInterval(adClock);if(game?.reward&&online){while(busy&&online)await new Promise(resolve=>setTimeout(resolve,50));if(online&&game?.reward)await act("reward-cancel")}adPending=false;render()});
document.querySelectorAll("[data-reward]").forEach(b=>b.onclick=()=>reward(b.dataset.reward));
function fmt(s){return Math.floor(s/60).toString().padStart(2,"0")+":"+String(s%60).padStart(2,"0")}
function animate(el,name){if(!el||matchMedia("(prefers-reduced-motion: reduce)").matches)return;el.classList.remove(name);void el.offsetWidth;el.classList.add(name);el.addEventListener("animationend",()=>el.classList.remove(name),{once:true})}
$("auto-finish").checked=autoFinish;
$("auto-finish").onchange=async()=>{const value=$("auto-finish").checked;autoFinish=value;localStorage.setItem("autoFinish",value?"1":"0");if(game&&!game.completed&&!game.outOfLives){if(!await act("auto-finish",{note:value})){autoFinish=!!game.autoFinish;localStorage.setItem("autoFinish",autoFinish?"1":"0");$("auto-finish").checked=autoFinish}}};
$("result-new").onclick=()=>{$("difficulty").value=$("result-difficulty").value;$("newgame").onclick()};
$("result-rank").onclick=()=>navigate("ranking");
$("result-daily").onclick=()=>{dailyMode="win";$("daily-open").onclick()};
$("weekly-rewards").onclick=()=>{dailyMode="weekly";$("daily-open").onclick()};
async function loadComparison(){
 if(!game?.completed||resultKey===game.id)return;const id=game.id,e=epoch;resultKey=id;
 try{const data=await api("comparison?difficulty="+levelsEnum[game.difficulty]+"&score="+game.currentScore+"&seconds="+game.seconds+"&rules="+game.rulesVersion);if(e===epoch&&game?.id===id){comparison=data;render()}}
 catch{if(game?.id===id){resultKey="";$("comparison").textContent="Не вдалося завантажити порівняння."}}
}
const levelsEnum=["Easy","Medium","Hard","Expert"];
function resultRow(parent,label,value){const r=document.createElement("div"),l=document.createElement("span"),v=document.createElement("strong");l.textContent=label;v.textContent=value;r.append(l,v);parent.append(r)}
function showResult(){
 const done=!!game?.completed, lost=!!game?.finalDefeat&&!adPending;
 $("result").hidden=!(done||lost);$("game-panel").hidden=done||lost;$("game-tools").hidden=done||lost;
 if(!done&&!lost)return;
 $("result-icon").textContent=done?"✓":"♡";$("result-title").textContent=done?"Головоломку розв’язано!":"Поразка";$("result-level").textContent=levels[game.difficulty];
 $("result-score").textContent=(done?game.currentScore:0).toLocaleString("uk-UA")+" балів";$("result-time").textContent="Час "+fmt(game.seconds)+" · помилки "+game.mistakes;
 const b=$("result-breakdown");b.replaceChildren();
 for(const [label,val] of [["База",game.baseScore],["Бонус за швидкість","+"+game.speedBonus],["Час","−"+Math.floor(game.seconds/3)],["Помилки","−"+75*game.mistakes],["Підказки","−"+150*game.hints],["Кандидати","−"+game.effectiveCandidatePenalty],["Автозавершення ("+game.autoFilled+" клітинок)","−"+game.autoFillPenalty],["Відновлення життя","−"+100*game.lifeRestores]])resultRow(b,label,val);
 const c=$("comparison");c.replaceChildren();
 if(!done||!comparison?.games){c.textContent=done?(comparison?"Поки недостатньо результатів.":online?"Завантаження статистики…":"Порівняння недоступне офлайн."):"Порівняння доступне для завершених ігор.";return}
 for(const [label,val] of [["Ігор / гравців",comparison.games+" / "+comparison.players],["Середній час",fmt(Math.round(comparison.averageSeconds))],["Медіанний час",fmt(Math.round(comparison.medianSeconds))],["Найкращий час",fmt(comparison.bestSeconds)],["Середній бал",Math.round(comparison.averageScore)],["Середні помилки",comparison.averageMistakes.toFixed(1)],["Середні підказки",comparison.averageHints.toFixed(1)],["Середні зміни кандидатів",comparison.averageCandidates.toFixed(1)],["Ваш бал не нижче ніж у",comparison.scorePercentile+"% ігор"],["Ваш час не довше ніж у",comparison.timePercentile+"% ігор"]])resultRow(c,label,val)
}
function updateRevive(){if(!game?.outOfLives||game.finalDefeat||!online)return;const left=Math.max(0,Math.ceil((reviveEnd-performance.now())/1000));$("revive-progress").value=left;$("cover-detail").textContent=game.reward?"Останній шанс використано.":left>0?"Останній шанс: "+left+" с":"Час вичерпано.";$("resume").disabled=busy||adPending||left===0;if(!left&&!game.reward&&online&&!busy&&!adPending)act("heartbeat")}
setInterval(updateRevive,200);
function updateClock(){const running=online&&game&&!game.paused&&!game.completed&&!game.outOfLives&&!game.reward&&!adPending;const seconds=(game?.seconds||0)+(running?Math.min(12,Math.floor((game.clockFraction||0)+(performance.now()-clockAnchor)/1000)):0);$("time").textContent=fmt(seconds);$("score").textContent=Math.max(0,(game?.currentScore||0)-Math.floor(seconds/3)+Math.floor((game?.seconds||0)/3)).toLocaleString("uk-UA");}
setInterval(updateClock,200);
function render(){
 const active=online&&game&&!game.paused&&!game.completed&&!game.outOfLives&&(!busy||!!pulse)&&!adPending;
 $("cover").hidden=!!(online&&game&&!game.paused&&!game.outOfLives);$("covertext").textContent=!online?"Немає з’єднання":game?.outOfLives?"Поразка":game?.paused?"Гра на паузі":busy?"Генеруємо поле…":"Створіть головоломку";
 $("resume").hidden=online&&!game;$("resume").disabled=busy;$("resume").textContent=!online?"Відновити з’єднання":game?.outOfLives?"Реклама · останній шанс":"Продовжити";$("cover-detail").textContent="";$("revive-progress").hidden=!game?.outOfLives;
 $("newgame").disabled=!online||busy;$("level").textContent=game?levels[game.difficulty]:"–";updateClock();$("lives").textContent=Array.from({length:3},(_,i)=>i<(game?.lives||0)?"♥":"♡").join(" ");$("penalties").textContent=game?"Час −"+Math.floor(game.seconds/3)+" · Помилки −"+75*game.mistakes+" · Підказки −"+150*game.hints+" · Кандидати −"+game.effectiveCandidatePenalty+" · Життя −"+100*game.lifeRestores+" · Авто −"+game.autoFillPenalty:""; $("hint").textContent="Підказка ("+(game?.hintCredits||0)+")";$("candidates").textContent="Кандидати ("+(game?.candidateCredits||0)+")";$("candidates").title=game?"Зміни: "+game.candidateDelta+" · −"+(2*game.candidateDelta)+" балів":"";document.querySelectorAll("[data-reward]").forEach(b=>{const count=b.dataset.reward==="life"?game?.lives:b.dataset.reward==="hint"?game?.hintCredits:game?.candidateCredits;b.hidden=!game||game.completed||game.outOfLives||count!==0||balance(b.dataset.reward)>0;b.disabled=!online||busy||adPending});
 $("rewards").hidden=Array.from(document.querySelectorAll("[data-reward]")).every(b=>b.hidden);$("result-daily").hidden=guestMode||!game?.completed||!daily?.packs.some(p=>p.milestone!=="weekly");$("weekly-rewards").hidden=guestMode||!daily?.packs.some(p=>p.milestone==="weekly");
 $("progress").textContent=game?(game.completed?"✓ Судоку розв’язано! ":"")+game.values.filter(Boolean).length+"/81 · Підказки: "+game.hints:"";
 buttons.forEach((b,i)=>{const v=game?.values[i]||0,r=Math.floor(i/9),c=i%9,sr=Math.floor(selected/9),sc=selected%9;
 const animation=["pop","shake","hint-pulse"].filter(k=>b.classList.contains(k)).join(" ");
 b.className="cell"+(c%3===2&&c!==8?" blockright":"")+(r%3===2&&r!==8?" blockbottom":"")+(game?.givens[i]?" given":"")+(i===selected?" selected":v&&v===game?.values[selected]?" same":r===sr||c===sc||Math.floor(r/3)===Math.floor(sr/3)&&Math.floor(c/3)===Math.floor(sc/3)?" peer":"");
 if(animation)b.classList.add(...animation.split(" "));b.classList.toggle("wrong",!!game?.wrong?.[i]);b.replaceChildren();if(v)b.textContent=v;else if(game?.notes[i]){let n=document.createElement("span");n.className="mininotes";for(let d=1;d<=9;d++){let k=document.createElement("span");k.textContent=game.notes[i]&(1<<d)?d:"";n.append(k)}b.append(n)}
 b.tabIndex=i===selected?0:-1;b.disabled=!online||!game||game.paused||game.outOfLives||adPending;b.setAttribute("aria-label","Рядок "+(r+1)+", стовпець "+(c+1)+": "+(v||"порожньо"));});
 document.querySelectorAll("#pad button,#tools button").forEach(b=>b.disabled=!active);Array.from($("pad").children).forEach((b,i)=>{const n=game?.remaining?.[i]??9;b.querySelector("small").textContent=n;b.classList.toggle("spent-digit",n===0);b.disabled=!active||(!note&&n===0);b.setAttribute("aria-label","Цифра "+(i+1)+", залишилось "+n)});$("auto-finish").checked=game?.autoFinish??autoFinish;$("auto-finish").disabled=!!game&&(busy||game.completed||game.outOfLives);
 document.querySelector('[data-action="undo"]').disabled=!active||!game?.canUndo;document.querySelector('[data-action="redo"]').disabled=!active||!game?.canRedo;$("notes").classList.toggle("active",note);showResult();updateRevive();
}
document.addEventListener("keydown",e=>{if(tab!=="play"||!online||!game||e.target.matches("input,select")||$("confirm").open||$("auth").open||$("ad").open||$("daily").open||$("help").open||adPending)return;
 let k=e.key.toLowerCase();if(k==="p"||k==="escape"){act(game.paused?"resume":"pause");e.preventDefault();return}if(game.paused||game.completed||game.outOfLives)return;
 if(e.ctrlKey&&k==="z")act(e.shiftKey?"redo":"undo");else if(e.ctrlKey&&k==="y")act("redo");else if(e.ctrlKey)return;
 else if(/^[1-9]$/.test(k))enter(Number(k),e.shiftKey);else if(["delete","backspace","0"].includes(k))enter(0);else if(k==="n"||k===" "){note=!note;render()}else if(k==="h")tool("hint");else if(k==="a")tool("candidates");
 else if(k.startsWith("arrow")){let r=Math.floor(selected/9),c=selected%9;if(k==="arrowleft")c--;if(k==="arrowright")c++;if(k==="arrowup")r--;if(k==="arrowdown")r++;selected=Math.max(0,Math.min(8,r))*9+Math.max(0,Math.min(8,c));render();buttons[selected].focus()}else return;e.preventDefault()});
function row(parent,values){let r=document.createElement("div");r.className="record";values.forEach(v=>{let s=document.createElement("span");s.textContent=v;r.append(s)});parent.append(r)}
async function navigate(t){if(t!==tab&&t!=="play"&&!await pauseForNavigation())return;tab=t;["play","history","ranking","weekly"].forEach(n=>$(n).hidden=n!==t);try{
 if(t==="history"&&!guestMode){let records=await api("history"),drafts=await api("desktop/drafts"),target=$("historyrows"),wins=records.filter(r=>r.outcome==="completed");target.replaceChildren();$("summary").textContent="Ігор: "+records.length+" · Перемог: "+wins.length+" · Балів: "+wins.reduce((s,r)=>s+r.score,0)+" · Час: "+fmt(records.reduce((s,r)=>s+r.game.seconds,0));let streak=0,best=0,perfect=0;for(const r of [...records].reverse()){if(r.outcome==="completed"){streak++;best=Math.max(best,streak);if(!r.game.mistakes&&!r.game.hints&&!r.game.candidateUses&&!r.game.autoFilled&&!r.game.lifeRestores)perfect++}else streak=0}$("summary").textContent+=" · Ідеальні: "+perfect+" · Серія: "+streak+" · Рекорд серії: "+best;row(target,["Дата / джерело","Складність","Результат / час","Оцінка"]);if(game&&!game.completed&&!game.finalDefeat)row(target,["Поточна · web",levels[game.difficulty],"У процесі · "+fmt(game.seconds),"–"]);drafts.forEach(r=>row(target,[new Date(r.updated).toLocaleString("uk-UA")+" · desktop",levels[r.game.puzzle.difficulty],"У процесі · "+fmt(r.game.seconds),"–"]));records.forEach(r=>row(target,[new Date(r.ended).toLocaleString("uk-UA")+" · "+r.source,levels[r.game.puzzle.difficulty],(r.outcome==="completed"?"Перемога":r.outcome==="defeated"?"Поразка":"Перервано")+" · "+fmt(r.game.seconds),r.score+" балів · помилки: "+r.game.mistakes+" · підказки: "+r.game.hints+" · кандидати: "+(r.game.candidateUses||0)+" · відновлення: "+(r.game.lifeRestores||0)]));}
 if(t==="ranking"){const mode=$("rankmode").value,metric=$("rankmetric").value;let ranks=await api("accumulated?all="+(mode==="all")+"&metric="+metric),target=$("rankrows");if(tab!=="ranking"||$("rankmode").value!==mode||$("rankmetric").value!==metric)return;target.replaceChildren();row(target,["Місце / гравець","Перемоги / ідеальні","Серія / рекорд","Бали"]);ranks.forEach((r,i)=>row(target,[(i+1)+". "+r.user,r.wins+" / "+r.perfectWins,r.currentStreak+" / "+r.bestStreak,r.score]));}
 if(t==="weekly"){const w=await api("weekly");if(tab!=="weekly")return;$("weekly-title").textContent="Тиждень від "+w.week;const target=$("weeklyrows");target.replaceChildren();row(target,["Місце","Гравець","Перемоги","Бали"]);w.leaders.forEach((r,i)=>row(target,[i+1,r.user,r.games,r.score]));const old=$("previous-week");old.replaceChildren();row(old,["Місце","Гравець","Перемоги","Бали"]);w.previousLeaders.forEach((r,i)=>row(old,[i+1,r.user,r.games,r.score]));await refreshDaily();}
 }catch(e){message(e.message)}}
$("rankmode").onchange=$("rankmetric").onchange=()=>navigate("ranking");
$("help-open").onclick=async()=>{if(await pauseForNavigation())$("help").showModal()};$("help-close").onclick=()=>$("help").close();
document.querySelectorAll("[data-tab]").forEach(b=>b.onclick=()=>navigate(b.dataset.tab));
render();if(token)start();else {resetSession();login(true)}
