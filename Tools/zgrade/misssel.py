def obvious(m):
    return (m['other']<0.3 and m['fill']>=0.6 and 40<=m['area']<=300 and m['rect_area']<=1.6*m['area']
            and m['rb']>=30 and m['in_terra']>=0.6 and m['ring_terra']<=0.25 and (m['lstd']>=10 or m['area']<120))
