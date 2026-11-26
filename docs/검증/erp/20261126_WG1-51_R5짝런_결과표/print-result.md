# 인쇄 측정 결과 — 런 R5 · 잡 r5-main-pair · 판 main
〔정정 · 앞 런(R6) 결과표 꼬리표의 「© NAVER Corp.」 는 배포 꾸러미 저작권 글에서 옮긴 표기였음 · 이번부터 글꼴 파일 저작권 기록 원문 인용으로 바꿈〕

- 판독기 양성 대조(먼저 · F4 ⑴ · HQA ②): **통과** — 기본 폼 글자 · 좌표 · 양식지 그림 해시 · 칸 값 있음 · 판독기 자체시험 통과
- 글꼴 관문(T8 · 판정 관문): **통과** — 글꼴 관문 통과 — 2×2 ⑷ 글꼴 섬 · 굵게 견본 없음 — T8 굵게 대조 미측정(사슬 그리기에 굵게 0)
- 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕
- 요청 이름 = Malgun Gothic · PDF 실제 글꼴 칸(사실 파일 값 그대로) = AAAAAA+PrintSubstitute-Bold, BAAAAA+PrintSubstitute-Regular, AAAAAA+PrintSubstitute-Regular
- 굵게 요청 → 600 파일: 굵게 파일 usWeightClass = 600(기대 600 · 700 아님)
- (기록만 · 판정 아님) 런 PDF 글꼴 이름(접두 뗀 값) = PrintSubstitute-Bold, PrintSubstitute-Regular · 굵게 이름 있음 = 예
- ㉤ FontName = 측정 · FontFamily = [1-V] ③ ⑤ 원문상 이 경로(Type0/CID)에선 안 씀 · 런 실측 미측정 · 내장본 이름 표 ID 1 「Malgun Gothic」 남음(원문)
- PDF 업로드 0(법무 재확인 3 · 결과표 · 사실 파일만): 사실 뽑은 뒤 지운 PDF 152 · 못 지움 0
- 대표 DB 조건 재현 = 미측정(고정 줄 · 작업지시서 §4-2 HQA ④)
- 🔴 멈춤: K3.estimate.settings main 초록 — 측정 재설계 · PM 보고 / K3.delivery.settings main 초록 — 측정 재설계 · PM 보고 / K3.sales_order.settings main 초록 — 측정 재설계 · PM 보고 / K3.purchase_order.settings main 초록 — 측정 재설계 · PM 보고 / K3.receipt.settings main 초록 — 측정 재설계 · PM 보고 / K3.purchase_return.settings main 초록 — 측정 재설계 · PM 보고 / K3.sales_return.settings main 초록 — 측정 재설계 · PM 보고 / ①(가) estimate main 초록 — 차림이 대표 화면 조건을 못 만듦 · 그 종 멈춤 · 측정 재설계 / ①(가) delivery main 초록 — 차림이 대표 화면 조건을 못 만듦 · 그 종 멈춤 · 측정 재설계 / ①(가) sales_order main 초록 — 차림이 대표 화면 조건을 못 만듦 · 그 종 멈춤 · 측정 재설계 / ①(가) purchase_order main 초록 — 차림이 대표 화면 조건을 못 만듦 · 그 종 멈춤 · 측정 재설계 / ①(가) receipt main 초록 — 차림이 대표 화면 조건을 못 만듦 · 그 종 멈춤 · 측정 재설계 / ①(가) purchase_return main 초록 — 차림이 대표 화면 조건을 못 만듦 · 그 종 멈춤 · 측정 재설계
- 멈춤 ⓐ 의 『측정이 대표 실패를 못 잡음』 은 main 기대가 빨강인 칸에만 건다 · 회귀 · 초록일 수 있음 칸의 main 초록은 정상
- 기록(멈춤 아님): K2.delivery.doc main 빨강(초록일 수 있음) — 기록 / K2.sales_order.doc main 빨강(초록일 수 있음) — 기록 / K2.purchase_order.doc main 빨강(초록일 수 있음) — 기록

| 칸 | 묶음 | main 기대 | 판정 | 사유 |
|---|---|---|---|---|
| K1.estimate.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · doc_date · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K1.estimate.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) · ○ 미리보기 창 경고 0 |
| K2.estimate.settings | ② | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K2.estimate.doc | ③(가) | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K3.estimate.settings | ② | 빨강 | 초록 | ○ 자리표 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K3.estimate.doc | ③(가) | 빨강 | 빨강 | ○ 양식 그림 있음(4분면 순서 · 종횡비) · × 그림만 아님(§59) — 낱말 55 · 선 51 〔기록: K3 안내 없음〕 |
| K4.estimate.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · doc_date · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K4.estimate.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 공급가액 · 공급가액 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K5.estimate.settings | ② | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| K5.estimate.doc | ③(가) | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| ST-basic.estimate.doc | ③(나) | 초록(회귀) | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · ○ 제목 크기 20pt(기준 20) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1) |
| ST-formal.estimate.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 0.0~19.9mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.estimate.doc · ST-simple.estimate.doc · ST-large.estimate.doc |
| ST-simple.estimate.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 18) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 0.75) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.estimate.doc · ST-formal.estimate.doc · ST-large.estimate.doc |
| ST-large.estimate.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 24) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1.5) · 품명 잘림 0 · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.estimate.doc · ST-formal.estimate.doc · ST-simple.estimate.doc |
| ST-ignore.estimate.doc | ③(나) 회귀 | 초록(회귀) | 초록 | ○ K2.estimate.doc 와 글자 · 자리 같음 |
| K1.delivery.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K1.delivery.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) · ○ 미리보기 창 경고 0 |
| K2.delivery.settings | ② | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K2.delivery.doc | ③(가) | 초록일 수 있음 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K3.delivery.settings | ② | 빨강 | 초록 | ○ 자리표 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K3.delivery.doc | ③(가) | 빨강 | 빨강 | ○ 양식 그림 있음(4분면 순서 · 종횡비) · × 그림만 아님(§59) — 낱말 55 · 선 51 〔기록: K3 안내 없음〕 |
| K4.delivery.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K4.delivery.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K5.delivery.settings | ② | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| K5.delivery.doc | ③(가) | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| ST-basic.delivery.doc | ③(나) | 초록(회귀) | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · ○ 제목 크기 20pt(기준 20) · 명조 글꼴 false · 제목 자리 0.0~33.2mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1) |
| ST-formal.delivery.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 0.0~33.2mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.delivery.doc · ST-simple.delivery.doc · ST-large.delivery.doc |
| ST-simple.delivery.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 18) · 명조 글꼴 false · 제목 자리 0.0~33.2mm(기준 왼쪽) · 머리 아래 선 1pt(기준 0.75) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.delivery.doc · ST-formal.delivery.doc · ST-large.delivery.doc |
| ST-large.delivery.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 24) · 명조 글꼴 false · 제목 자리 0.0~33.2mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1.5) · 품명 잘림 0 · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.delivery.doc · ST-formal.delivery.doc · ST-simple.delivery.doc |
| ST-ignore.delivery.doc | ③(나) 회귀 | 초록(회귀) | 초록 | ○ K2.delivery.doc 와 글자 · 자리 같음 |
| K1.sales_order.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K1.sales_order.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) · ○ 미리보기 창 경고 0 |
| K2.sales_order.settings | ② | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K2.sales_order.doc | ③(가) | 초록일 수 있음 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K3.sales_order.settings | ② | 빨강 | 초록 | ○ 자리표 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K3.sales_order.doc | ③(가) | 빨강 | 빨강 | ○ 양식 그림 있음(4분면 순서 · 종횡비) · × 그림만 아님(§59) — 낱말 55 · 선 51 〔기록: K3 안내 없음〕 |
| K4.sales_order.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K4.sales_order.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K5.sales_order.settings | ② | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| K5.sales_order.doc | ③(가) | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| ST-basic.sales_order.doc | ③(나) | 초록(회귀) | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · ○ 제목 크기 20pt(기준 20) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1) |
| ST-formal.sales_order.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 0.0~19.9mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.sales_order.doc · ST-simple.sales_order.doc · ST-large.sales_order.doc |
| ST-simple.sales_order.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 18) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 0.75) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.sales_order.doc · ST-formal.sales_order.doc · ST-large.sales_order.doc |
| ST-large.sales_order.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 24) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1.5) · 품명 잘림 0 · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.sales_order.doc · ST-formal.sales_order.doc · ST-simple.sales_order.doc |
| ST-ignore.sales_order.doc | ③(나) 회귀 | 초록(회귀) | 초록 | ○ K2.sales_order.doc 와 글자 · 자리 같음 |
| K1.purchase_order.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K1.purchase_order.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) · ○ 미리보기 창 경고 0 |
| K2.purchase_order.settings | ② | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K2.purchase_order.doc | ③(가) | 초록일 수 있음 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K3.purchase_order.settings | ② | 빨강 | 초록 | ○ 자리표 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K3.purchase_order.doc | ③(가) | 빨강 | 빨강 | ○ 양식 그림 있음(4분면 순서 · 종횡비) · × 그림만 아님(§59) — 낱말 55 · 선 51 〔기록: K3 안내 없음〕 |
| K4.purchase_order.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K4.purchase_order.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K5.purchase_order.settings | ② | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| K5.purchase_order.doc | ③(가) | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| ST-basic.purchase_order.doc | ③(나) | 초록(회귀) | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · ○ 제목 크기 20pt(기준 20) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1) |
| ST-formal.purchase_order.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 0.0~19.9mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.purchase_order.doc · ST-simple.purchase_order.doc · ST-large.purchase_order.doc |
| ST-simple.purchase_order.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 18) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 0.75) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.purchase_order.doc · ST-formal.purchase_order.doc · ST-large.purchase_order.doc |
| ST-large.purchase_order.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 24) · 명조 글꼴 false · 제목 자리 0.0~19.9mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1.5) · 품명 잘림 0 · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.purchase_order.doc · ST-formal.purchase_order.doc · ST-simple.purchase_order.doc |
| ST-ignore.purchase_order.doc | ③(나) 회귀 | 초록(회귀) | 초록 | ○ K2.purchase_order.doc 와 글자 · 자리 같음 |
| K1.receipt.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K1.receipt.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · doc_date · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) · ○ 미리보기 창 경고 0 |
| K2.receipt.settings | ② | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K2.receipt.doc | ③(가) | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K3.receipt.settings | ② | 빨강 | 초록 | ○ 자리표 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K3.receipt.doc | ③(가) | 빨강 | 빨강 | ○ 양식 그림 있음(4분면 순서 · 종횡비) · × 그림만 아님(§59) — 낱말 55 · 선 51 〔기록: K3 안내 없음〕 |
| K4.receipt.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K4.receipt.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · doc_date · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K5.receipt.settings | ② | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| K5.receipt.doc | ③(가) | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| ST-basic.receipt.doc | ③(나) | 초록(회귀) | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · ○ 제목 크기 20pt(기준 20) · 명조 글꼴 false · 제목 자리 0.0~33.2mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1) |
| ST-formal.receipt.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 0.0~33.2mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.receipt.doc · ST-simple.receipt.doc · ST-large.receipt.doc |
| ST-simple.receipt.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 18) · 명조 글꼴 false · 제목 자리 0.0~33.2mm(기준 왼쪽) · 머리 아래 선 1pt(기준 0.75) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.receipt.doc · ST-formal.receipt.doc · ST-large.receipt.doc |
| ST-large.receipt.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 24) · 명조 글꼴 false · 제목 자리 0.0~33.2mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1.5) · 품명 잘림 0 · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.receipt.doc · ST-formal.receipt.doc · ST-simple.receipt.doc |
| ST-ignore.receipt.doc | ③(나) 회귀 | 초록(회귀) | 초록 | ○ K2.receipt.doc 와 글자 · 자리 같음 |
| K1.purchase_return.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K1.purchase_return.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · doc_date · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) · ○ 미리보기 창 경고 0 |
| K2.purchase_return.settings | ② | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K2.purchase_return.doc | ③(가) | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K3.purchase_return.settings | ② | 빨강 | 초록 | ○ 자리표 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K3.purchase_return.doc | ③(가) | 빨강 | 빨강 | ○ 양식 그림 있음(4분면 순서 · 종횡비) · × 그림만 아님(§59) — 낱말 55 · 선 51 〔기록: K3 안내 없음〕 |
| K4.purchase_return.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K4.purchase_return.doc | ③(가) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: partner_name · doc_date · line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 그 그림 0 · ○ 미리보기 창 경고 0 |
| K5.purchase_return.settings | ② | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| K5.purchase_return.doc | ③(가) | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| ST-basic.purchase_return.doc | ③(나) | 초록(회귀) | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · ○ 제목 크기 20pt(기준 20) · 명조 글꼴 false · 제목 자리 0.0~13.3mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1) |
| ST-formal.purchase_return.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 0.0~13.3mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.purchase_return.doc · ST-simple.purchase_return.doc · ST-large.purchase_return.doc |
| ST-simple.purchase_return.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 18) · 명조 글꼴 false · 제목 자리 0.0~13.3mm(기준 왼쪽) · 머리 아래 선 1pt(기준 0.75) · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.purchase_return.doc · ST-formal.purchase_return.doc · ST-large.purchase_return.doc |
| ST-large.purchase_return.doc | ③(나) | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 24) · 명조 글꼴 false · 제목 자리 0.0~13.3mm(기준 왼쪽) · 머리 아래 선 1pt(기준 1.5) · 품명 잘림 0 · × 같은 PDF(글자 · 자리 · 크기 · 선): ST-basic.purchase_return.doc · ST-formal.purchase_return.doc · ST-simple.purchase_return.doc |
| ST-ignore.purchase_return.doc | ③(나) 회귀 | 초록(회귀) | 초록 | ○ K2.purchase_return.doc 와 글자 · 자리 같음 |
| K1.sales_return.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K1.sales_return.doc-i | ③(가) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| K1.sales_return.doc-j | ③(가) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| K2.sales_return.settings | ② | 초록일 수 있음 | 초록 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 5/5 상자 안 · ○ 자리표 0 · ○ 표준 표 머리 칸 밖 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K2.sales_return.doc-i | ③(가) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| K2.sales_return.doc-j | ③(가) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| K3.sales_return.settings | ② | 빨강 | 초록 | ○ 자리표 0 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K3.sales_return.doc-i | ③(가) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} 〔기록: K3 안내 없음〕 |
| K3.sales_return.doc-j | ③(가) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} 〔기록: K3 안내 없음〕 |
| K4.sales_return.settings | ② | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 칸 값 없음 또는 상자 밖: line.item_name · line.qty · total_amount · ○ 자리표 0 · × 표준 표 머리 칸 밖 품명 · 수량 · 공급가액 · 공급가액 · ○ 양식 그림 있음(4분면 순서 · 종횡비) |
| K4.sales_return.doc-i | ③(가) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| K4.sales_return.doc-j | ③(가) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| K5.sales_return.settings | ② | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| K5.sales_return.doc-i | ③(가) | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| K5.sales_return.doc-j | ③(가) | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9 · 갈래 F 뒤) |
| ST-basic.sales_return.doc-i | ③(나) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-basic.sales_return.doc-j | ③(나) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-formal.sales_return.doc-i | ③(나) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-formal.sales_return.doc-j | ③(나) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-simple.sales_return.doc-i | ③(나) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-simple.sales_return.doc-j | ③(나) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-large.sales_return.doc-i | ③(나) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-large.sales_return.doc-j | ③(나) | 빨강(C8) | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-ignore.sales_return.doc-i | ③(나) 회귀 | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| ST-ignore.sales_return.doc-j | ③(나) 회귀 | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| WM-가-on.estimate.settings | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-on.estimate.doc | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-off.estimate.settings | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「견적서」 · ○ 그 그림 0 |
| WM-가-off.estimate.doc | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「견적서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 |
| WM-나-on.estimate.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「견적서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-on.estimate.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「견적서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.estimate.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「견적서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.estimate.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「견적서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-가-on.delivery.settings | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-on.delivery.doc | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-off.delivery.settings | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「거래명세서」 · ○ 그 그림 0 |
| WM-가-off.delivery.doc | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「거래명세서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 |
| WM-나-on.delivery.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「거래명세서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-on.delivery.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「거래명세서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.delivery.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「거래명세서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.delivery.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「거래명세서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-가-on.sales_order.settings | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-on.sales_order.doc | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-off.sales_order.settings | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「수주서」 · ○ 그 그림 0 |
| WM-가-off.sales_order.doc | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「수주서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 |
| WM-나-on.sales_order.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「수주서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-on.sales_order.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「수주서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.sales_order.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「수주서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.sales_order.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「수주서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-가-on.purchase_order.settings | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-on.purchase_order.doc | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-off.purchase_order.settings | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「발주서」 · ○ 그 그림 0 |
| WM-가-off.purchase_order.doc | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「발주서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 |
| WM-나-on.purchase_order.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「발주서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-on.purchase_order.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「발주서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.purchase_order.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「발주서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.purchase_order.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「발주서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-가-on.receipt.settings | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-on.receipt.doc | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-off.receipt.settings | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「매입명세서」 · ○ 그 그림 0 |
| WM-가-off.receipt.doc | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「매입명세서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 |
| WM-나-on.receipt.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「매입명세서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-on.receipt.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「매입명세서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.receipt.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「매입명세서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.receipt.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「매입명세서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-가-on.purchase_return.settings | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-on.purchase_return.doc | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-off.purchase_return.settings | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · ○ 그 그림 0 |
| WM-가-off.purchase_return.doc | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 |
| WM-나-on.purchase_return.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-on.purchase_return.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.purchase_return.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.purchase_return.doc | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · 표지 「인쇄표지상사」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-가-on.sales_return.settings | ①(가) | 미확정 | 초록 | ○ 로고 그림 1(4분면 순서 · 알파 영역 종횡비 · 눈에 보이는 비 1.3333) · 실효 불투명도 0.120(알파 영역 안 평균) · 기준 0.12±0.02 〔그린 자리 alpha 0.12 · imgSMask true · smaskMean 0.5194 · smaskMeanIn 1 · aboxRatio 1.3333 · marginMaskMax 0 · 놓인 151.3417×151.3417mm · 마스크 1716×1716〕 |
| WM-가-on.sales_return.doc-i | ①(가) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| WM-가-on.sales_return.doc-j | ①(가) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| WM-가-off.sales_return.settings | ①(가) | 미확정 | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · ○ 그 그림 0 |
| WM-가-off.sales_return.doc-i | ①(가) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| WM-가-off.sales_return.doc-j | ①(가) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| WM-나-on.sales_return.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-on.sales_return.doc-i | ①(나) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| WM-나-on.sales_return.doc-j | ①(나) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| WM-나-off.sales_return.settings | ①(나) | 초록(정답) | 초록 | ○ 문서 확인 — 쪽 1 · 제목 「문서」 · ○ 그 그림 0 · ○ 안내 · 경고 글자 0 |
| WM-나-off.sales_return.doc-i | ①(나) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| WM-나-off.sales_return.doc-j | ①(나) | 빨강(C8) | 빨강 | 응답 404 · {"message":"저장된 전표가 아닙니다. 먼저 저장하시거나 목록에서 전표를 불러와 주십시오."} |
| OP.estimate.doc-wm | OP | 빨강 | 빨강 | 응답 500 · 단추 누름 뒤 PDF 0(내려받기 · PDF 응답 없음 · 오류 응답 500) |
| OP.estimate.doc-formal | OP | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 응답 500 · 단추 누름 뒤 PDF 0(내려받기 · PDF 응답 없음 · 오류 응답 500) |
| OP.estimate.route | OP 경로 | 기록(main 꼴 = GET …/pdf 1 · POST …/preview-pdf 0) | 해당 없음 | 기록: 경로 GET …/pdf 1 · POST …/preview-pdf 0 — main 꼴 |
| OP.delivery.doc-wm | OP | 빨강 | 빨강 | × 그림 객체 0(빈 투명 묶음 꼴 포함) 〔그린 자리 그린 자리 0〕 |
| OP.delivery.doc-formal | OP | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 84.4~125.5mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) |
| OP.delivery.route | OP 경로 | 기록(main 꼴 = GET …/pdf 1 · POST …/preview-pdf 0) | 해당 없음 | 기록: 경로 GET …/pdf 1 · POST …/preview-pdf 0 — main 꼴 |
| OP.purchase_order.doc-wm | OP | 빨강 | 빨강 | × 그림 객체 0(빈 투명 묶음 꼴 포함) 〔그린 자리 그린 자리 0〕 |
| OP.purchase_order.doc-formal | OP | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 93.0~116.9mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) |
| OP.purchase_order.route | OP 경로 | 기록(main 꼴 = GET …/pdf 1 · POST …/preview-pdf 0) | 해당 없음 | 기록: 경로 GET …/pdf 1 · POST …/preview-pdf 0 — main 꼴 |
| OP.receipt.doc-wm | OP | 빨강 | 빨강 | × 그림 객체 0(빈 투명 묶음 꼴 포함) 〔그린 자리 그린 자리 0〕 |
| OP.receipt.doc-formal | OP | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 ○ 칸 1/1 상자 안 · × 제목 크기 20pt(기준 22) · 명조 글꼴 false(필요) · 격식 글꼴 이름 없음(기준 NotoSerifKR-Bold · 종류 기록 없음) · 제목 자리 84.4~125.5mm(기준 가운데) · 머리 아래 선 1pt(기준 1.5 + 0.5) |
| OP.receipt.route | OP 경로 | 기록(main 꼴 = GET …/pdf 1 · POST …/preview-pdf 0) | 해당 없음 | 기록: 경로 GET …/pdf 1 · POST …/preview-pdf 0 — main 꼴 |
| OP.purchase_return.doc-wm | OP | 빨강 | 빨강 | × 문서 확인 — 엉뚱한 종 제목 「반품처리서」 — 입구 결함 〔그린 자리 그린 자리 0〕 |
| OP.purchase_return.doc-formal | OP | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 문서 확인 — 엉뚱한 종 제목 「반품처리서」 — 입구 결함 |
| OP.purchase_return.route | OP 경로 | 기록(main 꼴 = GET …/pdf 1 · POST …/preview-pdf 0) | 해당 없음 | 기록: 경로 GET …/pdf 1 · POST …/preview-pdf 0 — main 꼴 |
| OP.sales_return.doc-wm | OP | 빨강 | 빨강 | × 문서 확인 — 엉뚱한 종 제목 「반품처리서」 — 입구 결함 〔그린 자리 그린 자리 0〕 |
| OP.sales_return.doc-formal | OP | 빨강 | 빨강 | 〔대체 글꼴 기준 측정 · 맑은 고딕(Microsoft) 아님 · 글리프 = 나눔고딕(SIL OFL 1.1) · 글꼴 파일 저작권 기록 원문: Copyright © 2011 NHN Corporation〕 × 문서 확인 — 엉뚱한 종 제목 「반품처리서」 — 입구 결함 |
| OP.sales_return.route | OP 경로 | 기록(main 꼴 = GET …/pdf 1 · POST …/preview-pdf 0) | 해당 없음 | 기록: 경로 GET …/pdf 1 · POST …/preview-pdf 0 — main 꼴 |
| A5-basic.estimate.doc | 측정 A5 | 측정 칸 | 측정 칸 | 측정: ① 응답 500({"error":"\uC11C\uBC84 \uC624\uB958\uAC00 \uBC1C\uC0DD\uD588\uC2B5\uB2C8\uB2E4. ) · ② · ③ 못 잼 |
| A5-formal.estimate.doc | 측정 A5 | 측정 칸 | 측정 칸 | 측정: ① 응답 500({"error":"\uC11C\uBC84 \uC624\uB958\uAC00 \uBC1C\uC0DD\uD588\uC2B5\uB2C8\uB2E4. ) · ② · ③ 못 잼 |
| A5-simple.estimate.doc | 측정 A5 | 측정 칸 | 측정 칸 | 측정: ① 응답 500({"error":"\uC11C\uBC84 \uC624\uB958\uAC00 \uBC1C\uC0DD\uD588\uC2B5\uB2C8\uB2E4. ) · ② · ③ 못 잼 |
| A5-large.estimate.doc | 측정 A5 | 측정 칸 | 측정 칸 | 측정: ① 응답 500({"error":"\uC11C\uBC84 \uC624\uB958\uAC00 \uBC1C\uC0DD\uD588\uC2B5\uB2C8\uB2E4. ) · ② · ③ 못 잼 |
| M8-A5.estimate.doc | M8 큰글씨 | 기록 | 미측정 | 응답 500 |
| M8-A4LR25.estimate.doc | M8 큰글씨 | 기록 | 미측정 | 응답 500 |
| M4-B1.estimate.doc | M4 큰글씨 | 기록 | 미측정 | 판독기 · 품목 행 닻 「1,000,000.0」 없음 — 숫자 꼴 다름 · 엉뚱한 전표 못 가름 |
| M7-basic-B1.estimate.doc | M7 품명 | 기록 | 초록 | ○ 품명 100자 전부 · 쪽 1 |
| M7-large-B1.estimate.doc | M7 품명 | 기록 | 초록 | ○ 품명 100자 전부 · 쪽 1 |
| M4-B0.estimate.doc | M4 큰글씨 | 기록 | 미측정 | 판독기 · 품목 행 닻 「1,000,000.0」 없음 — 숫자 꼴 다름 · 엉뚱한 전표 못 가름 |
| M7-basic-B0.estimate.doc | M7 품명 | 기록 | 빨강 | × 품명 잘림 — 앞 96/100자만 |
| M7-large-B0.estimate.doc | M7 품명 | 기록 | 빨강 | × 품명 잘림 — 앞 96/100자만 |
| Z8c.estimate.concurrent | 동시 2건 | 기록(동시 2건 ≠ 경합 없음 증명) | 해당 없음 | 기록: 동시 2건 격식 200 PDF · 거래명세서 200 PDF → 이어서 격식 200 PDF · 거래명세서 200 PDF · 동시 2건 ≠ 경합 없음 증명 |
| D1.estimate.api | D 재발 방지 | 빨강 | 빨강 | 비기본 둘째 저장 POST 500 |
| D2.estimate.api | D 재발 방지 | 빨강 | 빨강 | 첫 지우기 DELETE 204 · 비기본 새로 저장 POST 201 → 두 번째 지우기 DELETE 500 |
| D3.estimate.api | D 재발 방지 | 빨강 | 빨강 | 목록 GET 200 → 다른 양식 기본 지정 POST 500 |
| S.estimate.editor | S | 안 보임(기록) | 해당 없음 | 기록: {"coordTableVisible":false,"pdfAccept":false} |
| YN1.estimate.editor | Y/N | 문 없음 | 문 없음 · 판정 제외 | PDF 양식 올리기 문 없음(C9) |

| 묶음 판정 | 판정 | 칸 수 |
|---|---|---|
| ①(가) 견적서 객체 확인 — 켬 A 모든 길 있음 · 끔 B 모든 길 없음 | 초록 | 4 |
| ①(나) 견적서 로고 없음 + 켬 — 아무것도 안 보임(회귀 기준) | 초록 | 4 |
| ①(가) 거래명세서 객체 확인 — 켬 A 모든 길 있음 · 끔 B 모든 길 없음 | 초록 | 4 |
| ①(나) 거래명세서 로고 없음 + 켬 — 아무것도 안 보임(회귀 기준) | 초록 | 4 |
| ①(가) 수주서 객체 확인 — 켬 A 모든 길 있음 · 끔 B 모든 길 없음 | 초록 | 4 |
| ①(나) 수주서 로고 없음 + 켬 — 아무것도 안 보임(회귀 기준) | 초록 | 4 |
| ①(가) 발주서 객체 확인 — 켬 A 모든 길 있음 · 끔 B 모든 길 없음 | 초록 | 4 |
| ①(나) 발주서 로고 없음 + 켬 — 아무것도 안 보임(회귀 기준) | 초록 | 4 |
| ①(가) 매입처리 객체 확인 — 켬 A 모든 길 있음 · 끔 B 모든 길 없음 | 초록 | 4 |
| ①(나) 매입처리 로고 없음 + 켬 — 아무것도 안 보임(회귀 기준) | 초록 | 4 |
| ①(가) 매입 반품 객체 확인 — 켬 A 모든 길 있음 · 끔 B 모든 길 없음 | 초록 | 4 |
| ①(나) 매입 반품 로고 없음 + 켬 — 아무것도 안 보임(회귀 기준) | 초록 | 4 |
| ①(가) 판매 반품 객체 확인 — 켬 A 모든 길 있음 · 끔 B 모든 길 없음 | 빨강 | 6 |
| ①(나) 판매 반품 로고 없음 + 켬 — 아무것도 안 보임(회귀 기준) | 빨강 | 6 |

판정 끝 — 양성 대조 통과 · 초록 83 · 빨강 96 · 미측정 4(그중 시간 상한 · 차림 순서 못 끝냄 0) · 차림 불가 0 · 문 없음 16 · 판정 제외 0 · 해당 없음 8 · 바뀐 모양 0 · 분모 밖 28 · 멈춤 13