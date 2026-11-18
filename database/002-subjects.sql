-- Public product catalogue only. No accounts, learner records or payments.
INSERT INTO public.guru_subjects(id,name,grades,description,price_monthly_zar)
VALUES
('mathematics','Mathematics',ARRAY[8,9,10,11,12],'Mathematics for Grades 8–12.',290),
('maths-literacy','Maths Literacy',ARRAY[10,11,12],'Mathematical Literacy for Grades 10–12.',290),
('physical-science','Physical Science',ARRAY[10,11,12],'Physical Science for Grades 10–12.',290),
('afrikaans','Afrikaans',ARRAY[11,12],'Afrikaans for Grades 11–12.',290)
ON CONFLICT(id) DO NOTHING;
