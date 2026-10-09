const REPORT_API=SUPABASE_URL+'/functions/v1/timeclock-report-api';

function reportDateYmd(d){
  const z=n=>String(n).padStart(2,'0');
  return `${d.getFullYear()}-${z(d.getMonth()+1)}-${z(d.getDate())}`;
}

async function refreshReportRecipientLine(){
  const line=document.getElementById('reportRecipientLine');
  if(!line)return;
  try{
    const {data,error}=await sb.from('app_settings').select('key,value').in('key',['payroll_email_recipient','payroll_email_additional']);
    if(error)throw error;
    const m=Object.fromEntries((data||[]).map(x=>[x.key,x.value]));
    const primary=String(m.payroll_email_recipient||'').trim();
    const additional=String(m.payroll_email_additional||'').trim();
    const all=[primary,additional].filter(Boolean).join(', ');
    line.textContent=all?`Report recipient: ${all}`:'No payroll report recipient configured.';
  }catch(e){
    line.textContent='Unable to load report recipient.';
  }
}

async function generatePayrollReportNow(){
  const btn=document.getElementById('sendReportNowBtn');
  const msg=document.getElementById('sendReportMsg');
  if(!btn||!msg)return;
  const period=periodsList()[Number(document.getElementById('payrollPeriod').value||0)];
  btn.disabled=true;
  btn.textContent='Preparing...';
  msg.textContent='';
  try{
    const {data}=await sb.auth.getSession();
    const token=data?.session?.access_token;
    if(!token)throw new Error('Your admin session has expired. Sign in again.');
    const r=await fetch(REPORT_API+'/api/payroll/report',{
      method:'POST',
      headers:{'Content-Type':'application/json','Authorization':'Bearer '+token,'apikey':SUPABASE_KEY},
      body:JSON.stringify({start:reportDateYmd(period.start),end:reportDateYmd(period.end)})
    });
    const j=await r.json().catch(()=>({}));
    if(!r.ok||j.ok===false)throw new Error(j.detail||'Unable to prepare payroll report.');
    msg.textContent=j.delivery_ready?'Payroll report sent.':'Payroll report is ready. Email delivery will be connected next.';
  }catch(e){
    msg.textContent='Error: '+String(e.message||e);
  }finally{
    btn.disabled=false;
    btn.textContent='Send Report Now';
  }
}

(function initPayrollReportControls(){
  const payroll=document.getElementById('page-payroll');
  const exportBtn=document.getElementById('exportCsvBtn');
  if(!payroll||!exportBtn)return;

  const btn=document.createElement('button');
  btn.id='sendReportNowBtn';
  btn.type='button';
  btn.textContent='Send Report Now';
  btn.style.marginRight='10px';
  exportBtn.parentNode.insertBefore(btn,exportBtn);
  btn.addEventListener('click',generatePayrollReportNow);

  const toolbar=payroll.querySelector('.toolbar');
  if(toolbar){
    const info=document.createElement('div');
    info.style.margin='-4px 0 18px';
    info.innerHTML='<span id="reportRecipientLine" class="muted">Loading report recipient...</span><span id="sendReportMsg" class="inline-msg" style="margin-left:14px"></span>';
    toolbar.insertAdjacentElement('afterend',info);
  }
  refreshReportRecipientLine();
  document.getElementById('saveSettingsBtn')?.addEventListener('click',()=>setTimeout(refreshReportRecipientLine,350));
})();
