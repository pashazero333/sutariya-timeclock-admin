function payrollData(){
  const per=selectedPeriod('payrollPeriod');
  const rows=punches.filter(p=>p.clock_out&&inPeriod(p,per.start,per.end));
  const m=new Map();
  for(const p of rows){
    if(!m.has(p.employee_id))m.set(p.employee_id,{id:p.employee_id,count:0,raw:0,rounded:0,shifts:[]});
    const x=m.get(p.employee_id);
    x.count++;
    x.raw+=Number(p.raw_hours||0);
    x.rounded+=Number(p.rounded_hours||0);
    x.shifts.push(p);
  }
  return [...m.values()].sort((a,b)=>empName(a.id).localeCompare(empName(b.id)));
}

function payrollDay(v){
  return new Date(v).toLocaleDateString('en-US',{weekday:'short',month:'short',day:'numeric'});
}

function payrollTime(v){
  return v?new Date(v).toLocaleTimeString('en-US',{hour:'numeric',minute:'2-digit'}):'ACTIVE';
}

function togglePayrollEmployee(id){
  const row=document.getElementById('payroll-detail-'+id);
  const btn=document.getElementById('payroll-toggle-'+id);
  if(!row||!btn)return;
  const opening=row.classList.contains('hidden');
  row.classList.toggle('hidden',!opening);
  btn.textContent=(opening?'▼ ':'▶ ')+empName(id);
  btn.setAttribute('aria-expanded',opening?'true':'false');
}

function renderPayroll(){
  const data=payrollData();
  const raw=data.reduce((a,x)=>a+x.raw,0);
  const rounded=data.reduce((a,x)=>a+x.rounded,0);
  payrollRaw.textContent=`Raw ${raw.toFixed(2)} hrs`;
  payrollRounded.textContent=`Rounded ${rounded.toFixed(2)} hrs`;
  if(!data.length){
    payrollBody.innerHTML='<tr><td colspan="4" class="empty">No completed punches in this payroll period.</td></tr>';
    return;
  }
  payrollBody.innerHTML=data.map(x=>{
    const detail=x.shifts.slice().sort((a,b)=>new Date(a.clock_in)-new Date(b.clock_in)).map(p=>`
      <tr>
        <td>${payrollDay(p.clock_in)}</td>
        <td>${payrollTime(p.clock_in)}</td>
        <td>${payrollTime(p.clock_out)}</td>
        <td>${hrs(p.raw_hours)}</td>
        <td><strong>${hrs(p.rounded_hours)}</strong></td>
      </tr>`).join('');
    return `
      <tr>
        <td><button id="payroll-toggle-${x.id}" class="payroll-employee-toggle" type="button" aria-expanded="false" onclick="togglePayrollEmployee(${x.id})">▶ ${empName(x.id)}</button></td>
        <td>${x.count}</td>
        <td>${hrs(x.raw)}</td>
        <td><strong>${hrs(x.rounded)}</strong></td>
      </tr>
      <tr id="payroll-detail-${x.id}" class="hidden payroll-detail-row">
        <td colspan="4">
          <div class="payroll-detail-box">
            <table class="table payroll-shift-table">
              <thead><tr><th>Day / Date</th><th>Clock In</th><th>Clock Out</th><th>Raw</th><th>Rounded</th></tr></thead>
              <tbody>${detail}</tbody>
            </table>
          </div>
        </td>
      </tr>`;
  }).join('');
}

if(typeof loadPayroll!=='undefined')loadPayroll.onclick=renderPayroll;
