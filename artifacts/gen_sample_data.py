#!/usr/bin/env python3
"""Generate sample data SQL for HitPan ERP."""
import uuid
import random
from datetime import date, timedelta

TENANT = '452ca266-97b9-4cd1-a0ac-2f37830c81f6'
WAREHOUSE = 'ea3a8f69-3b9d-11f1-aced-54b2038e28b9'
EMPLOYEE_IDS = [
    'd2922473-3892-11f1-aced-54b2038e28b9',
    'd292722b-3892-11f1-aced-54b2038e28b9',
    'd2927f9b-3892-11f1-aced-54b2038e28b9',
]
NOW = '2026-04-18 10:00:00.000000'

random.seed(42)

def uid():
    return str(uuid.uuid4())

def rand_date(start, end):
    delta = (end - start).days
    d = start + timedelta(days=random.randint(0, delta))
    return d.strftime('%Y-%m-%d')

def esc(s):
    return s.replace("'", "\\'")

# ========== PARTNERS ==========
regions = {
    '서울': ['강남구', '서초구', '마포구', '종로구', '영등포구', '송파구', '용산구', '중구'],
    '경기': ['성남시', '수원시', '용인시', '고양시', '안양시', '부천시', '화성시', '평택시'],
    '부산': ['해운대구', '수영구', '남구', '동구', '사하구'],
    '대구': ['수성구', '달서구', '중구', '북구'],
    '인천': ['남동구', '연수구', '서구', '중구'],
    '광주': ['서구', '북구', '남구'],
    '대전': ['유성구', '서구', '중구'],
    '울산': ['남구', '중구', '울주군'],
    '세종': ['세종시'],
    '제주': ['제주시', '서귀포시'],
}

sales_names = [
    '(주)한국전자', '삼성물산', 'LG생활건강', '현대모비스', 'SK하이닉스',
    '(주)대한통운', '포스코인터내셔널', '한화솔루션', '롯데케미칼', 'CJ제일제당',
    '(주)동원F&B', '농심', '오뚜기', '풀무원', '삼양식품',
    '(주)코리아텍', '한국정밀', '대성산업', '금호타이어', '넥센타이어',
    '(주)동아전기', '서울반도체', '한국파워', '대림산업', 'GS건설',
    '(주)현대건설', '대우건설', '삼성엔지니어링', 'SK건설', '포스코건설',
    '(주)한진중공업', 'STX조선', '대한조선', '한국기계', '두산중공업',
    '(주)효성', '코오롱인더스트리', 'KCC', '한솔케미칼', '이수화학',
    '(주)태광산업', '동국제강', '세아제강', '현대제철', '한국철강',
    '(주)미래에셋', '한화투자', 'KB증권', 'NH투자증권', '삼성증권',
    '(주)한국유리', '동양물산', '삼표시멘트', '쌍용C&E', '성신양회',
    '(주)동서식품', '빙그레', '매일유업', '남양유업', '서울우유',
    '(주)지코어', '알파정밀', '베타텍', '감마전자', '델타시스템',
    '(주)오메가솔루션', '시그마테크', '엡실론전기', '제타로보틱스', '에타소프트',
    '(주)세타엔지니어링', '아이오타바이오', '카파메디컬', '람다에너지', '뮤테크놀로지',
    '(주)뉴미디어', '크시오프틱스', '파이전자', '로케미칼', '시그마원',
    '(주)타우시스템', '업실론네트웍스', '파이시큐리티', '카이데이터', '오메가프로',
    '(주)알파웨이브', '베타사이언스', '감마파워', '델타일렉트로닉스', '에타에너텍',
    '(주)그린텍', '블루오션', '레드스타', '골드텍', '실버라인',
    '(주)퍼스트원', '세컨드웨이브', '서드포인트', '포스데이', '피프스에이지',
]

purchase_names = [
    '(주)원자재무역', '글로벌소재', '아시아부품', '대한소재', '한국원료',
    '(주)정밀부품', '하이테크소재', '원진물산', '동양소재', '서울부품',
    '(주)한국금속', '대성금속', '삼화금속', '진흥스틸', '영풍금속',
    '(주)제일플라스틱', '한국수지', '동양화학소재', '삼성화학', '현대케미',
    '(주)한국전선', '대한케이블', '동양전선', '서울전선', '부산전선',
    '(주)한국고무', '삼화고무', '대한실리콘', '코리아폴리머', '동양고무',
    '(주)한국베어링', '대한기어', '서울스프링', '부산볼트', '대구너트',
    '(주)인천패스너', '광주와셔', '대전나사', '울산체인', '세종피팅',
    '(주)제주소재', '강원부품', '충북원료', '충남자재', '전북소재',
    '(주)전남부품', '경북원료', '경남자재', '대구소재', '부산원료',
    '(주)서울공업', '경기공업', '인천공업', '대전공업', '광주공업',
    '(주)한국PCB', '동양전장', '서울모터', '부산센서', '대구LED',
]

both_names = [
    '(주)통합전자', '올인원텍', '풀서비스', '토탈솔루션', '완성시스템',
    '(주)한국종합', '대한올림푸스', '서울엔터프라이즈', '부산인더스트리', '대구매뉴팩처',
    '(주)인천커머스', '광주트레이딩', '대전유통', '울산물류', '세종글로벌',
    '(주)원스탑코리아', '쓰리식스티', '투게더텍', '유나이트웍스', '넥스트레벨',
    '(주)코리아파트너', '동양무역', '아시아커머스', '글로벌트레이드', '퍼시픽무역',
    '(주)한라산업', '태평양무역', '대서양통상', '북극성상사', '남십자무역',
    '(주)하나상사', '둘리무역', '셋째통상', '넷째글로벌', '다섯째인터내셔널',
    '(주)여섯번째상사', '일곱무역', '여덟통상', '아홉글로벌', '열번째인터',
]

partner_ids = []
lines = []
lines.append("-- ========== PARTNERS (200) ==========")

def gen_biz_no():
    return f'{random.randint(100,999)}-{random.randint(10,99)}-{random.randint(10000,99999)}'

def gen_tel():
    area = random.choice(['02','031','032','033','041','042','043','044','051','052','053','054','055','061','062','063','064'])
    return f'{area}-{random.randint(100,999)}-{random.randint(1000,9999)}'

biz_types = ['제조업','도소매업','서비스업','건설업','운수업','정보통신업','전자상거래']
biz_items_list = ['전자부품','기계부품','화학소재','금속가공','플라스틱','식품','의류','건자재','자동차부품','반도체']
ceo_surnames = ['김','이','박','최','정','강','조','윤','장','임','한','오','서','신','권','황','안','송','류','홍']
ceo_given = ['민수','영희','철수','지은','현우','수진','동혁','미영','성호','은정','태준','혜진','준서','나영','승민']
banks = ['국민은행','신한은행','하나은행','우리은행','농협은행','기업은행','SC은행','카카오뱅크']

all_partner_data = []
for i, name in enumerate(sales_names):
    all_partner_data.append((name, 'sales'))
for i, name in enumerate(purchase_names):
    all_partner_data.append((name, 'purchase'))
for i, name in enumerate(both_names):
    all_partner_data.append((name, 'both'))

sales_partner_ids = []
purchase_partner_ids = []

for idx, (pname, ptype) in enumerate(all_partner_data):
    pid = uid()
    partner_ids.append(pid)
    if ptype in ('sales', 'both'):
        sales_partner_ids.append(pid)
    if ptype in ('purchase', 'both'):
        purchase_partner_ids.append(pid)

    code = f'P-{idx+100:04d}'
    region = random.choice(list(regions.keys()))
    district = random.choice(regions[region])
    ceo = random.choice(ceo_surnames) + random.choice(ceo_given)
    biz_type = random.choice(biz_types)
    biz_item = random.choice(biz_items_list)
    tel = gen_tel()
    fax = gen_tel()
    email = f'info{idx}@{pname.replace("(주)","").replace(" ","").lower()[:8]}.co.kr'
    addr = f'{region} {district} {random.choice(["대로","로","길"])} {random.randint(1,500)}'
    credit = random.choice([1000000, 5000000, 10000000, 30000000, 50000000, 100000000])
    pay_terms = random.choice([10, 15, 30, 45, 60])
    bank = random.choice(banks)
    acct = f'{random.randint(100,999)}-{random.randint(10,99)}-{random.randint(100000,999999)}'
    manager = random.choice(ceo_surnames) + random.choice(ceo_given)
    manager_tel = f'010-{random.randint(1000,9999)}-{random.randint(1000,9999)}'
    grade = random.choice(['A','B','C','D'])

    lines.append(
        f"INSERT INTO partners (partner_id, tenant_id, partner_code, partner_name, partner_type, "
        f"biz_no, ceo_name, biz_type, biz_item, tel, fax, email, address, address_detail, "
        f"credit_limit, payment_terms, bank_name, bank_account, account_holder, is_active, "
        f"memo, created_at, created_by, updated_at, updated_by, is_deleted, zip_code, "
        f"manager_name, manager_tel, price_grade, tax_type, row_version) VALUES ("
        f"'{pid}', '{TENANT}', '{code}', '{esc(pname)}', '{ptype}', "
        f"'{gen_biz_no()}', '{ceo}', '{biz_type}', '{biz_item}', '{tel}', '{fax}', '{email}', "
        f"'{addr}', '', {credit:.2f}, {pay_terms}, '{bank}', '{acct}', '{ceo}', 1, "
        f"NULL, '{NOW}', NULL, '{NOW}', NULL, 0, '{random.randint(10000,99999)}', "
        f"'{manager}', '{manager_tel}', '{grade}', 'taxable', 0);"
    )

# ========== ITEMS ==========
lines.append("")
lines.append("-- ========== ITEMS (200) ==========")

product_names = [
    '전동드릴 18V', '충전기 220V', '모터 어셈블리 A', '전자저울 30kg', '디지털 온도계',
    'LED 조명등 36W', 'USB 충전케이블', '블루투스 스피커', '무선 마우스', '기계식 키보드',
    '모니터 24인치', '프린터 잉크젯', '스캐너 A4', '외장하드 1TB', 'USB허브 4포트',
    '멀티탭 6구', '전선릴 30m', '에어컨 벽걸이형', '제습기 16L', '공기청정기 HEPA',
    '선풍기 16인치', '온풍기 2kW', '전기히터', '전기포트 1.7L', '전자레인지',
    '믹서기 1L', '토스터기', '다리미 스팀', '진공청소기', '로봇청소기',
    '드론 촬영용', '액션카메라', 'CCTV 카메라', 'IP카메라 실내용', '인터폰',
    '전자잠금장치', '도어락 디지털', '화재감지기', '가스감지기', '온습도센서',
    '전동킥보드', '전동자전거', '배터리팩 48V', '충전스테이션', '파워뱅크 20000mAh',
    'UPS 무정전전원', '안정기 LED용', '변압기 단상', '인버터 3kW', '컨버터 DC-DC',
    '태블릿 10인치', '노트북 14인치', '미니PC', '서버랙 42U', '네트워크스위치 24p',
    'WiFi 공유기', 'LAN케이블 Cat6', '광케이블 SM', '패치판넬 24p', 'PDU 8구',
    '전동공구세트', '임팩트렌치', '그라인더 4인치', '절단기 14인치', '용접기 아크',
    '에어컴프레셔', '페인트스프레이건', '열풍기 산업용', '집진기 이동식', '세척기 고압',
    '3D프린터 FDM', '레이저커터', 'CNC밀링', '방전가공기', '사출성형기',
]

material_names = [
    '저항 10kΩ 1/4W', '저항 4.7kΩ', '저항 100Ω', '저항 1MΩ', '저항 220Ω',
    '콘덴서 100μF 16V', '콘덴서 10μF 50V', '콘덴서 1μF', '콘덴서 470μF', '콘덴서 22pF',
    'LED 적색 5mm', 'LED 녹색 3mm', 'LED 백색 SMD', 'LED 청색 파워', 'LED RGB 모듈',
    'PCB 기판 단면', 'PCB 기판 양면', 'PCB 기판 다층', 'FPC 기판', '알루미늄 기판',
    '트랜지스터 NPN', '트랜지스터 PNP', 'MOSFET N채널', 'MOSFET P채널', 'IGBT 모듈',
    '다이오드 정류용', '제너다이오드 5.1V', 'LED 드라이버IC', '마이크로컨트롤러 ARM', 'FPGA Spartan',
    '커넥터 USB-C', '커넥터 micro-USB', '커넥터 JST 2핀', '커넥터 RJ45', '커넥터 DB9',
    '전선 AWG22 적', '전선 AWG18 흑', '실드케이블 4심', '동축케이블 RG58', '리본케이블 20핀',
    '볼트 M4x10', '볼트 M6x20', '볼트 M8x30', '볼트 M10x40', '볼트 M12x50',
    '너트 M4', '너트 M6', '너트 M8', '너트 M10', '너트 M12',
    '와셔 M4', '와셔 M6', '와셔 M8', '스프링와셔 M6', '스프링와셔 M8',
    '스프링 인장 50mm', '스프링 압축 30mm', '오링 25mm', '패킹 실리콘', '가스켓 고무',
]

semi_names = [
    '모터 코어 조립체', 'PCB 제어보드 ASSY', '배터리 셀 모듈', '센서 모듈 온도', '센서 모듈 습도',
    'LED 모듈 어셈블리', '전원부 ASSY', '디스플레이 모듈', '통신 모듈 WiFi', '통신 모듈 BLE',
    '기어박스 소형', '기어박스 중형', '실린더 유압', '밸브 어셈블리', '펌프 모듈',
    '히트싱크 ASSY', '팬 모듈 40mm', '팬 모듈 80mm', '냉각 모듈', '필터 어셈블리',
    '케이싱 상부', '케이싱 하부', '브라켓 고정', '마운트 방진', '패널 전면',
    '하네스 메인', '하네스 보조', '케이블 어셈블리', '커넥터 ASSY', '단자대 모듈',
]

finished_names = [
    '스마트 컨트롤러 SC-100', '산업용 센서 IS-200', '전동 액추에이터 EA-300', '자동화 모듈 AM-400', '로봇 그리퍼 RG-500',
    '스마트 미터 SM-100', '전력 모니터 PM-200', 'IoT 게이트웨이 IG-300', '환경 센서 ES-400', '무선 중계기 WR-500',
    '산업용 PC IPC-100', '터치 패널 TP-200', 'HMI 디스플레이 HD-300', 'PLC 컨트롤러 PC-400', '서보 드라이버 SD-500',
    '인버터 시스템 IV-100', '전원 공급기 PS-200', 'UPS 시스템 UP-300', '배전반 DB-400', '제어반 CB-500',
    '측정기 멀티미터', '오실로스코프 DSO', '스펙트럼 분석기', '신호 발생기', '전원 분석기',
    '용접 로봇 WR-100', '이송 로봇 TR-200', '검사 장비 IE-300', '포장기 PK-400', '라벨러 LB-500',
]

item_ids = []
lines_items = []

all_items = []
for n in product_names:
    all_items.append((n, 'product'))
for n in material_names:
    all_items.append((n, 'material'))
for n in semi_names:
    all_items.append((n, 'semi_finished'))
for n in finished_names:
    all_items.append((n, 'finished'))

for idx, (iname, itype) in enumerate(all_items):
    iid = uid()
    item_ids.append(iid)
    code = f'I-{idx+100:04d}'

    if itype == 'material':
        pp = random.randint(100, 5000)
        sp = int(pp * random.uniform(1.2, 1.5))
    elif itype == 'semi_finished':
        pp = random.randint(3000, 20000)
        sp = int(pp * random.uniform(1.3, 1.6))
    elif itype == 'finished':
        pp = random.randint(10000, 50000)
        sp = int(pp * random.uniform(1.4, 2.0))
    else:  # product
        pp = random.randint(1000, 30000)
        sp = int(pp * random.uniform(1.2, 1.8))

    unit = random.choice(['EA','SET','BOX','KG','M','ROL','PKG']) if itype != 'material' else random.choice(['EA','PCS','BOX','PKG','ROL'])
    safe = random.choice([10, 20, 50, 100, 200, 500])
    group = random.choice(['전자','기계','화학','일반','포장','전기','광학'])
    spec_val = random.choice(['', 'KS규격', 'ISO9001', 'CE인증', 'UL인증', ''])

    lines_items.append(
        f"INSERT INTO items (item_id, tenant_id, item_code, item_name, item_type, "
        f"unit, purchase_price, sale_price, standard_price, cost_price, tax_type, "
        f"safety_stock, safe_stock, is_active, created_at, created_by, updated_at, updated_by, "
        f"is_deleted, item_group, spec, row_version, auto_order_enabled, auto_order_qty, barcode) VALUES ("
        f"'{iid}', '{TENANT}', '{code}', '{esc(iname)}', '{itype}', "
        f"'{unit}', {pp:.2f}, {sp:.2f}, {sp:.2f}, {pp:.2f}, 'taxable', "
        f"{safe:.3f}, {safe:.3f}, 1, '{NOW}', NULL, '{NOW}', NULL, "
        f"0, '{group}', '{spec_val}', 0, 0, 0.00, NULL);"
    )

lines.extend(lines_items)

# ========== QUOTATIONS (50) ==========
lines.append("")
lines.append("-- ========== QUOTATIONS (50) ==========")

start_d = date(2026, 2, 1)
end_d = date(2026, 4, 20)
statuses_qt = ['draft']*30 + ['converted']*15 + ['submitted']*5

quote_ids = []
for i in range(50):
    qid = uid()
    quote_ids.append(qid)
    qdate = rand_date(start_d, end_d)
    valid = (date.fromisoformat(qdate) + timedelta(days=30)).strftime('%Y-%m-%d')
    qno = f'QT-{qdate.replace("-","")}-{i+1:03d}'
    pid = random.choice(sales_partner_ids)
    eid = random.choice(EMPLOYEE_IDS)
    st = statuses_qt[i]

    num_items = random.randint(1, 5)
    chosen_items = random.sample(range(len(item_ids)), num_items)

    total = 0
    vat_total = 0
    item_lines = []
    for si, ci in enumerate(chosen_items):
        qty = random.randint(1, 100)
        price = random.choice([1000, 2000, 3000, 5000, 8000, 10000, 15000, 20000, 30000, 50000])
        amt = qty * price
        vat = int(amt * 0.1)
        total += amt
        vat_total += vat
        item_lines.append(
            f"INSERT INTO quotation_items (id, quote_id, item_id, unit, qty, unit_price, "
            f"discount_rate, amount, vat_amount, sort_order) VALUES ("
            f"'{uid()}', '{qid}', '{item_ids[ci]}', 'EA', {qty:.3f}, {price:.2f}, "
            f"0.00, {amt:.2f}, {vat:.2f}, {si});"
        )

    converted_id = 'NULL'
    lines.append(
        f"INSERT INTO quotations (quote_id, tenant_id, quote_no, partner_id, employee_id, "
        f"quote_date, valid_until, status, total_amount, vat_amount, memo, "
        f"created_at, created_by, updated_at, updated_by, is_deleted) VALUES ("
        f"'{qid}', '{TENANT}', '{qno}', '{pid}', '{eid}', "
        f"'{qdate}', '{valid}', '{st}', {total:.2f}, {vat_total:.2f}, NULL, "
        f"'{NOW}', NULL, '{NOW}', NULL, 0);"
    )
    lines.extend(item_lines)

# ========== SALES ORDERS (50) ==========
lines.append("")
lines.append("-- ========== SALES ORDERS (50) ==========")

so_ids = []
for i in range(50):
    oid = uid()
    so_ids.append(oid)
    odate = rand_date(start_d, end_d)
    ddate = (date.fromisoformat(odate) + timedelta(days=random.randint(7,30))).strftime('%Y-%m-%d')
    ono = f'SO-{odate.replace("-","")}-{i+1:03d}'
    pid = random.choice(sales_partner_ids)
    eid = random.choice(EMPLOYEE_IDS)

    num_items = random.randint(1, 5)
    chosen_items = random.sample(range(len(item_ids)), num_items)

    total = 0
    vat_total = 0
    item_lines = []
    for ci in chosen_items:
        qty = random.randint(1, 50)
        price = random.choice([1000, 2000, 5000, 8000, 10000, 15000, 20000, 30000])
        amt = qty * price
        vat = int(amt * 0.1)
        total += amt
        vat_total += vat
        item_lines.append(
            f"INSERT INTO sales_order_items (order_item_id, order_id, tenant_id, item_id, "
            f"ordered_qty, delivered_qty, unit_price, supply_amount, vat_amount, item_status) VALUES ("
            f"'{uid()}', '{oid}', '{TENANT}', '{item_ids[ci]}', "
            f"{qty:.3f}, 0.000, {price:.2f}, {amt:.2f}, {vat:.2f}, 'pending');"
        )

    lines.append(
        f"INSERT INTO sales_orders (order_id, tenant_id, order_no, partner_id, employee_id, "
        f"order_date, delivery_date, status, total_amount, vat_amount, memo, "
        f"created_at, created_by, updated_at, updated_by, is_deleted) VALUES ("
        f"'{oid}', '{TENANT}', '{ono}', '{pid}', '{eid}', "
        f"'{odate}', '{ddate}', 'draft', {total:.2f}, {vat_total:.2f}, NULL, "
        f"'{NOW}', NULL, '{NOW}', NULL, 0);"
    )
    lines.extend(item_lines)

# ========== SALES DELIVERIES (100) ==========
lines.append("")
lines.append("-- ========== SALES DELIVERIES (100) ==========")

sd_statuses = ['draft']*70 + ['confirmed']*20 + ['invoiced']*10

for i in range(100):
    did = uid()
    ddate = rand_date(start_d, end_d)
    dno = f'SD-{ddate.replace("-","")}-{i+1:03d}'
    pid = random.choice(sales_partner_ids)
    eid = random.choice(EMPLOYEE_IDS)
    st = sd_statuses[i]

    num_items = random.randint(1, 5)
    chosen_items = random.sample(range(len(item_ids)), num_items)

    total = 0
    vat_total = 0
    item_lines = []
    for ci in chosen_items:
        qty = random.randint(1, 30)
        price = random.choice([1000, 2000, 5000, 8000, 10000, 15000, 20000, 30000])
        amt = qty * price
        vat = int(amt * 0.1)
        total += amt
        vat_total += vat
        item_lines.append(
            f"INSERT INTO sales_delivery_items (delivery_item_id, delivery_id, tenant_id, "
            f"item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount) VALUES ("
            f"'{uid()}', '{did}', '{TENANT}', "
            f"'{item_ids[ci]}', '{WAREHOUSE}', {qty:.3f}, {price:.2f}, {amt:.2f}, {vat:.2f});"
        )

    lines.append(
        f"INSERT INTO sales_deliveries (delivery_id, tenant_id, delivery_no, partner_id, employee_id, "
        f"delivery_date, source_type, status, total_amount, vat_amount, memo, "
        f"created_at, created_by, updated_at, updated_by, is_deleted) VALUES ("
        f"'{did}', '{TENANT}', '{dno}', '{pid}', '{eid}', "
        f"'{ddate}', 'direct', '{st}', {total:.2f}, {vat_total:.2f}, NULL, "
        f"'{NOW}', NULL, '{NOW}', NULL, 0);"
    )
    lines.extend(item_lines)

# ========== PURCHASE ORDERS (50) ==========
lines.append("")
lines.append("-- ========== PURCHASE ORDERS (50) ==========")

for i in range(50):
    poid = uid()
    podate = rand_date(start_d, end_d)
    edate = (date.fromisoformat(podate) + timedelta(days=random.randint(7,30))).strftime('%Y-%m-%d')
    pono = f'PO-{podate.replace("-","")}-{i+1:03d}'
    pid = random.choice(purchase_partner_ids)
    eid = random.choice(EMPLOYEE_IDS)

    num_items = random.randint(1, 5)
    chosen_items = random.sample(range(len(item_ids)), num_items)

    total = 0
    vat_total = 0
    item_lines = []
    for ci in chosen_items:
        qty = random.randint(10, 500)
        price = random.choice([500, 1000, 2000, 3000, 5000, 8000, 10000])
        amt = qty * price
        vat = int(amt * 0.1)
        total += amt
        vat_total += vat
        item_lines.append(
            f"INSERT INTO purchase_order_items (po_item_id, po_id, tenant_id, item_id, "
            f"ordered_qty, received_qty, unit_price, supply_amount, vat_amount, warehouse_id, item_status) VALUES ("
            f"'{uid()}', '{poid}', '{TENANT}', '{item_ids[ci]}', "
            f"{qty:.3f}, 0.000, {price:.2f}, {amt:.2f}, {vat:.2f}, '{WAREHOUSE}', 'pending');"
        )

    lines.append(
        f"INSERT INTO purchase_orders (po_id, tenant_id, po_no, partner_id, employee_id, "
        f"po_date, expected_date, status, total_amount, vat_amount, memo, "
        f"created_at, created_by, updated_at, updated_by, is_deleted) VALUES ("
        f"'{poid}', '{TENANT}', '{pono}', '{pid}', '{eid}', "
        f"'{podate}', '{edate}', 'draft', {total:.2f}, {vat_total:.2f}, NULL, "
        f"'{NOW}', NULL, '{NOW}', NULL, 0);"
    )
    lines.extend(item_lines)

# ========== PURCHASE RECEIPTS (50) ==========
lines.append("")
lines.append("-- ========== PURCHASE RECEIPTS (50) ==========")

pr_statuses = ['draft']*30 + ['confirmed']*15 + ['cancelled']*5

for i in range(50):
    rid = uid()
    rdate = rand_date(start_d, end_d)
    rno = f'PR-{rdate.replace("-","")}-{i+1:03d}'
    pid = random.choice(purchase_partner_ids)
    st = pr_statuses[i]

    num_items = random.randint(1, 5)
    chosen_items = random.sample(range(len(item_ids)), num_items)

    total = 0
    vat_total = 0
    item_lines = []
    for ci in chosen_items:
        qty = random.randint(10, 200)
        price = random.choice([500, 1000, 2000, 3000, 5000, 8000])
        amt = qty * price
        vat = int(amt * 0.1)
        total += amt
        vat_total += vat
        item_lines.append(
            f"INSERT INTO purchase_receipt_items (receipt_item_id, receipt_id, tenant_id, "
            f"item_id, warehouse_id, qty, unit_price, supply_amount, vat_amount) VALUES ("
            f"'{uid()}', '{rid}', '{TENANT}', "
            f"'{item_ids[ci]}', '{WAREHOUSE}', {qty:.3f}, {price:.2f}, {amt:.2f}, {vat:.2f});"
        )

    lines.append(
        f"INSERT INTO purchase_receipts (receipt_id, tenant_id, receipt_no, partner_id, "
        f"receipt_date, source_type, status, total_amount, vat_amount, memo, "
        f"created_at, created_by) VALUES ("
        f"'{rid}', '{TENANT}', '{rno}', '{pid}', "
        f"'{rdate}', 'direct', '{st}', {total:.2f}, {vat_total:.2f}, NULL, "
        f"'{NOW}', NULL);"
    )
    lines.extend(item_lines)

# Write all
with open('C:/Users/소순근/Desktop/hitpan-erp/artifacts/sample-data.sql', 'w', encoding='utf-8') as f:
    f.write("SET NAMES utf8mb4;\n")
    f.write("SET FOREIGN_KEY_CHECKS = 0;\n\n")
    f.write('\n'.join(lines))
    f.write("\n\nSET FOREIGN_KEY_CHECKS = 1;\n")

print(f"Generated {len(lines)} SQL statements")
print(f"Partners: {len(partner_ids)}")
print(f"Items: {len(item_ids)}")
print(f"Sales partners: {len(sales_partner_ids)}")
print(f"Purchase partners: {len(purchase_partner_ids)}")
